using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Legge da un file OpenType/TrueType tutto quello che serve alla pagina dello stile:
/// i caratteri codificati (cmap) divisi per blocco Unicode, le forme alternative raggiungibili con le
/// funzioni OpenType (GSUB: legature, set stilistici, alternative, maiuscoletto, numeri…) e le misure verticali.
/// </summary>
public static class GlyphReader
{
    /// <summary>Oltre questo numero di punti di codice ci fermiamo (font CJK enormi o tabelle danneggiate).</summary>
    private const int MaxCodePoints = 120_000;

    /// <summary>Per ogni funzione mostriamo al massimo questo numero di forme.</summary>
    private const int MaxPerFeature = 2_000;

    public static GlyphSet Read(byte[] data)
    {
        var tables = OpenTypeReader.Tables(data);
        if (tables is null) return new GlyphSet { Error = "This file’s format (woff2, for example) doesn’t allow listing its characters." };

        byte[]? T(string tag) { try { return tables.TryGetValue(tag, out var f) ? f() : null; } catch { return null; } }

        var cmap = ReadCmap(T("cmap"));

        var metrics = ReadMetrics(T("head"), T("hhea"), T("OS/2"));
        var maxp = T("maxp");
        int total = maxp is { Length: >= 6 } ? U16(maxp, 4) : 0;

        // dal glifo al carattere: per scrivere il testo che fa comparire una forma alternativa
        var glyphToText = new Dictionary<int, int>();
        foreach (var (cp, gid) in cmap.OrderBy(kv => IsPrivate(kv.Key) ? 1 : 0).ThenBy(kv => kv.Key))
            glyphToText.TryAdd(gid, cp);

        int id = 0;
        var all = new List<GlyphItem>();
        GlyphItem Add(string text, int? cp, string? feature, int? glyph)
        {
            var it = new GlyphItem(id++, text, cp, feature, glyph);
            all.Add(it);
            return it;
        }

        var blocks = new List<GlyphGroup>();
        foreach (var g in cmap.Keys.Where(Visible).Order().GroupBy(cp => UnicodeBlocks.Of(cp)).OrderBy(g => g.Key.Key))
        {
            var items = g.Select(cp => Add(char.ConvertFromUtf32(cp), cp, null, cmap[cp])).ToList();
            blocks.Add(new GlyphGroup($"b{g.Key.Key:X}", g.Key.Name, null, items));
        }

        var visible = new HashSet<int>(blocks.SelectMany(b => b.Items).Select(i => cmap[i.CodePoint!.Value]));
        var features = new List<GlyphGroup>();
        try
        {
            foreach (var (tag, title, seqs) in ReadFeatures(T("GSUB"), T("name")))
            {
                var seen = new HashSet<string>();
                var items = new List<GlyphItem>();
                foreach (var (glyphs, value, outs) in seqs)
                {
                    if (items.Count >= MaxPerFeature) break;
                    var sb = new StringBuilder();
                    bool ok = true;
                    foreach (var gid in glyphs)
                    {
                        if (!glyphToText.TryGetValue(gid, out var cp) || !Visible(cp)) { ok = false; break; }
                        sb.Append(char.ConvertFromUtf32(cp));
                    }
                    if (!ok) continue;
                    var css = $"'{tag}' {value}";
                    var text = sb.ToString();
                    if (seen.Add(text + "|" + css)) items.Add(Add(text, null, css, outs.Length == 1 ? outs[0] : null));
                    visible.UnionWith(outs);
                }
                if (items.Count > 0) features.Add(new GlyphGroup("f" + tag, title, tag, items));
            }
        }
        catch { /* GSUB danneggiata: mostriamo almeno i caratteri codificati */ }

        // i caratteri dell'area privata, nei font professionali, sono spesso le stesse forme alternative elencate sotto
        if (features.Count > 0)
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].Key is "bE000" or "bF0000")
                    blocks[i] = blocks[i] with { Note = "often the same alternate forms listed further down" };

        var bounds = GlyphBounds.Create(T);
        return new GlyphSet
        {
            Metrics = metrics, Blocks = blocks, Features = features, TotalGlyphs = total, Reachable = visible.Count, All = all,
            InkOf = bounds is null ? null : bounds.Of,
        };
    }

    private static bool IsPrivate(int cp) => cp is >= 0xE000 and <= 0xF8FF or >= 0xF0000;

    /// <summary>Si vede qualcosa? Niente caratteri di controllo, spazi e surrogati.</summary>
    private static bool Visible(int cp)
    {
        if (cp is >= 0xD800 and <= 0xDFFF || cp > 0x10FFFF) return false;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        return cat is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate);
    }

    // ---- misure ----

    private static GlyphMetrics? ReadMetrics(byte[]? head, byte[]? hhea, byte[]? os2)
    {
        if (head is not { Length: >= 20 }) return null;
        int upm = U16(head, 18);
        if (upm is < 16 or > 16384) return null;
        int asc = 0, desc = 0;
        if (hhea is { Length: >= 8 }) { asc = S16(hhea, 4); desc = S16(hhea, 6); }
        if ((asc == 0 && desc == 0) && os2 is { Length: >= 72 }) { asc = S16(os2, 68); desc = S16(os2, 70); }
        if (asc <= 0) asc = (int)(upm * 0.8);
        if (desc >= 0) desc = -(int)(upm * 0.2);
        int? cap = null, x = null;
        if (os2 is { Length: >= 90 } && U16(os2, 0) >= 2)
        {
            int xv = S16(os2, 86), cv = S16(os2, 88);
            if (xv > 0) x = xv;
            if (cv > 0) cap = cv;
        }
        return new GlyphMetrics(upm, asc, desc, cap, x);
    }

    // ---- cmap ----

    private static Dictionary<int, int> ReadCmap(byte[]? t)
    {
        var map = new Dictionary<int, int>();
        if (t is not { Length: >= 4 }) return map;
        int n = U16(t, 2);
        // preferenze: Unicode completo (formato 12), poi Unicode di base (formato 4), poi Mac Roman
        int best = -1, bestScore = 0;
        for (int i = 0; i < n; i++)
        {
            int r = 4 + i * 8;
            if (r + 8 > t.Length) break;
            int pid = U16(t, r), eid = U16(t, r + 2), off = (int)U32(t, r + 4);
            if (off < 0 || off + 2 > t.Length) continue;
            int fmt = U16(t, off);
            int score = (pid, eid, fmt) switch
            {
                (3, 10, 12) or (0, 4, 12) or (0, 6, 12) => 6,
                (0, _, 12) => 5,
                (3, 1, 4) => 4,
                (0, _, 4) => 3,
                (3, 0, 4) => 2,        // carattere "symbol": i codici stanno in F000–F0FF
                (1, 0, 0) or (1, 0, 6) => 1,
                _ => 0,
            };
            if (score > bestScore) { bestScore = score; best = off; }
        }
        if (best < 0) return map;
        try
        {
            switch (U16(t, best))
            {
                case 4: Format4(t, best, map); break;
                case 12: Format12(t, best, map); break;
                case 0: for (int c = 0; c < 256 && best + 6 + c < t.Length; c++) if (t[best + 6 + c] != 0) map[c] = t[best + 6 + c]; break;
                case 6:
                {
                    int first = U16(t, best + 6), count = U16(t, best + 8);
                    for (int i = 0; i < count && best + 10 + i * 2 + 2 <= t.Length; i++) { int g = U16(t, best + 10 + i * 2); if (g != 0) map[first + i] = g; }
                    break;
                }
            }
        }
        catch { /* tabella troncata: teniamo quello che abbiamo letto */ }
        return map;
    }

    private static void Format4(byte[] t, int o, Dictionary<int, int> map)
    {
        int segX2 = U16(t, o + 6);
        int ends = o + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
        for (int s = 0; s < segX2 / 2; s++)
        {
            int end = U16(t, ends + s * 2), start = U16(t, starts + s * 2);
            int delta = S16(t, deltas + s * 2), rangeAt = ranges + s * 2, range = U16(t, rangeAt);
            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                int g;
                if (range == 0) g = (c + delta) & 0xFFFF;
                else
                {
                    int addr = rangeAt + range + 2 * (c - start);
                    if (addr + 2 > t.Length) break;
                    g = U16(t, addr);
                    if (g != 0) g = (g + delta) & 0xFFFF;
                }
                if (g != 0) map[c] = g;
                if (map.Count > MaxCodePoints) return;
            }
        }
    }

    private static void Format12(byte[] t, int o, Dictionary<int, int> map)
    {
        long groups = U32(t, o + 12);
        for (long i = 0; i < groups; i++)
        {
            int r = o + 16 + (int)i * 12;
            if (r + 12 > t.Length) break;
            long start = U32(t, r), end = U32(t, r + 4), g = U32(t, r + 8);
            for (long c = start; c <= end && c <= 0x10FFFF; c++)
            {
                map[(int)c] = (int)(g + c - start);
                if (map.Count > MaxCodePoints) return;
            }
        }
    }

    // ---- GSUB ----

    private static readonly string[] Order =
    [
        "liga", "dlig", "hlig", "ss01", "ss02", "ss03", "ss04", "ss05", "ss06", "ss07", "ss08", "ss09", "ss10",
        "ss11", "ss12", "ss13", "ss14", "ss15", "ss16", "ss17", "ss18", "ss19", "ss20", "salt", "swsh", "cswh", "calt",
        "smcp", "c2sc", "pcap", "c2pc", "unic", "case", "titl", "onum", "lnum", "pnum", "tnum", "zero", "frac",
        "numr", "dnom", "sups", "subs", "sinf", "ordn", "ornm", "nalt", "hist", "init", "medi", "fina", "isol",
    ];

    private static readonly Dictionary<string, string> Names = new()
    {
        ["liga"] = "Standard ligatures", ["dlig"] = "Discretionary ligatures", ["hlig"] = "Historical ligatures",
        ["salt"] = "Stylistic alternates", ["swsh"] = "Swash", ["cswh"] = "Contextual swash",
        ["calt"] = "Contextual alternates", ["smcp"] = "Small capitals", ["c2sc"] = "Capitals to small capitals",
        ["pcap"] = "Petite capitals", ["c2pc"] = "Capitals to petite capitals", ["unic"] = "Unicase",
        ["case"] = "Case-sensitive forms", ["titl"] = "Titling", ["onum"] = "Oldstyle figures",
        ["lnum"] = "Lining figures", ["pnum"] = "Proportional figures", ["tnum"] = "Tabular figures",
        ["zero"] = "Slashed zero", ["frac"] = "Fractions", ["numr"] = "Numerators", ["dnom"] = "Denominators",
        ["sups"] = "Superscript", ["subs"] = "Subscript", ["sinf"] = "Scientific inferiors", ["ordn"] = "Ordinals",
        ["ornm"] = "Ornaments", ["nalt"] = "Alternate annotation forms (circled, negative…)", ["hist"] = "Historical forms",
        ["init"] = "Initial forms", ["medi"] = "Medial forms", ["fina"] = "Terminal forms", ["isol"] = "Isolated forms",
    };

    private static string? TitleOf(string tag, string? uiName)
    {
        if (tag.StartsWith("ss") && int.TryParse(tag[2..], out var ss))
            return uiName is null ? $"Stylistic set {ss}" : $"Stylistic set {ss} — {uiName}";
        if (tag.StartsWith("cv") && int.TryParse(tag[2..], out var cv))
            return uiName is null ? $"Character variant {cv}" : $"Character variant {cv} — {uiName}";
        return Names.GetValueOrDefault(tag);
    }

    /// <summary>Per ogni funzione interessante: le sequenze di glifi che la funzione trasforma e il valore da accendere.</summary>
    private static IEnumerable<(string Tag, string Title, List<(int[] Glyphs, int Value, int[] Out)> Seqs)> ReadFeatures(byte[]? t, byte[]? name)
    {
        if (t is not { Length: >= 10 }) yield break;
        int featureList = U16(t, 6), lookupList = U16(t, 8);
        if (featureList == 0 || lookupList == 0 || featureList + 2 > t.Length || lookupList + 2 > t.Length) yield break;

        // la stessa funzione compare una volta per ogni scrittura e lingua: uniamo i suoi lookup
        var byTag = new Dictionary<string, (SortedSet<int> Lookups, string? UiName)>();
        int fc = U16(t, featureList);
        for (int i = 0; i < fc; i++)
        {
            int r = featureList + 2 + i * 6;
            if (r + 6 > t.Length) break;
            string tag = Encoding.ASCII.GetString(t, r, 4);
            int fo = featureList + U16(t, r + 4);
            if (fo + 4 > t.Length) continue;
            if (TitleOf(tag, null) is null) continue;
            if (!byTag.TryGetValue(tag, out var entry)) byTag[tag] = entry = ([], null);
            int paramsOff = U16(t, fo), count = U16(t, fo + 2);
            for (int k = 0; k < count && fo + 4 + k * 2 + 2 <= t.Length; k++) entry.Lookups.Add(U16(t, fo + 4 + k * 2));
            if (paramsOff != 0 && entry.UiName is null && (tag.StartsWith("ss") || tag.StartsWith("cv")) && name is not null
                && fo + paramsOff + 4 <= t.Length)
            {
                int nameId = U16(t, fo + paramsOff + 2);
                byTag[tag] = (entry.Lookups, NameById(name, nameId));
            }
        }

        int lookupCount = U16(t, lookupList);
        static int Rank(string tag) { int ix = Array.IndexOf(Order, tag); return ix >= 0 ? ix : 1000; }
        foreach (var tag in byTag.Keys.OrderBy(Rank).ThenBy(k => k, StringComparer.Ordinal))
        {
            var (lookups, ui) = byTag[tag];
            var seqs = new List<(int[], int, int[])>();
            foreach (var li in lookups)
            {
                if (li >= lookupCount) continue;
                int lo = lookupList + U16(t, lookupList + 2 + li * 2);
                if (lo + 6 > t.Length) continue;
                int type = U16(t, lo), subCount = U16(t, lo + 4);
                for (int s = 0; s < subCount && lo + 6 + s * 2 + 2 <= t.Length; s++)
                {
                    int so = lo + U16(t, lo + 6 + s * 2), st = type;
                    if (st == 7 && so + 8 <= t.Length) { st = U16(t, so + 2); so += (int)U32(t, so + 4); }   // estensione
                    if (so + 6 > t.Length) continue;
                    Subtable(t, so, st, seqs);
                    if (seqs.Count > MaxPerFeature * 2) break;
                }
            }
            if (seqs.Count > 0) yield return (tag, TitleOf(tag, ui)!, seqs);
        }
    }

    private static void Subtable(byte[] t, int so, int type, List<(int[], int, int[])> seqs)
    {
        int fmt = U16(t, so);
        var cov = Coverage(t, so + U16(t, so + 2));
        int At(int o) => o + 2 <= t.Length ? U16(t, o) : 0;
        switch (type)
        {
            case 1:   // sostituzione semplice: ogni glifo coperto cambia forma
            {
                int delta = fmt == 1 ? S16(t, so + 4) : 0;
                for (int i = 0; i < cov.Count; i++)
                {
                    int g = fmt == 1 ? (cov[i] + delta) & 0xFFFF : i < At(so + 4) ? At(so + 6 + i * 2) : -1;
                    seqs.Add(([cov[i]], 1, g >= 0 ? [g] : []));
                }
                break;
            }
            case 2:   // sostituzione multipla: un glifo diventa più glifi
            {
                int count = At(so + 4);
                for (int i = 0; i < cov.Count && i < count; i++)
                {
                    int seq = so + At(so + 6 + i * 2), n = Math.Min(At(seq), 16);
                    var outs = new int[n];
                    for (int k = 0; k < n; k++) outs[k] = At(seq + 2 + k * 2);
                    seqs.Add(([cov[i]], 1, outs));
                }
                break;
            }
            case 3:   // alternative: ogni glifo ha più forme, scelte con il valore della funzione
            {
                int count = At(so + 4);
                for (int i = 0; i < cov.Count && i < count; i++)
                {
                    int set = so + At(so + 6 + i * 2);
                    if (set + 2 > t.Length) continue;
                    int n = Math.Min(At(set), 20);
                    for (int v = 1; v <= n; v++) seqs.Add(([cov[i]], v, [At(set + 2 + (v - 1) * 2)]));
                }
                break;
            }
            case 4:   // legature: più glifi diventano uno
            {
                int count = At(so + 4);
                for (int i = 0; i < cov.Count && i < count; i++)
                {
                    int set = so + At(so + 6 + i * 2);
                    if (set + 2 > t.Length) continue;
                    int ligs = At(set);
                    for (int k = 0; k < ligs && set + 2 + k * 2 + 2 <= t.Length; k++)
                    {
                        int lig = set + At(set + 2 + k * 2);
                        if (lig + 4 > t.Length) continue;
                        int comps = At(lig + 2);
                        if (comps is < 1 or > 8 || lig + 4 + (comps - 1) * 2 > t.Length) continue;
                        var seq = new int[comps];
                        seq[0] = cov[i];
                        for (int c = 1; c < comps; c++) seq[c] = At(lig + 4 + (c - 1) * 2);
                        seqs.Add((seq, 1, [At(lig)]));
                    }
                }
                break;
            }
        }
    }

    private static List<int> Coverage(byte[] t, int o)
    {
        var list = new List<int>();
        if (o + 4 > t.Length) return list;
        int fmt = U16(t, o), n = U16(t, o + 2);
        if (fmt == 1)
            for (int i = 0; i < n && o + 4 + i * 2 + 2 <= t.Length; i++) list.Add(U16(t, o + 4 + i * 2));
        else if (fmt == 2)
            for (int i = 0; i < n && o + 4 + i * 6 + 6 <= t.Length; i++)
            {
                int a = U16(t, o + 4 + i * 6), b = U16(t, o + 6 + i * 6);
                for (int g = a; g <= b && list.Count < 20_000; g++) list.Add(g);
            }
        return list;
    }

    /// <summary>Un nome della tabella name (per i nomi dei set stilistici scelti dal disegnatore).</summary>
    private static string? NameById(byte[] t, int id)
    {
        if (t.Length < 6) return null;
        int count = U16(t, 2), strOff = U16(t, 4);
        string? best = null; int bestScore = 0;
        for (int i = 0; i < count; i++)
        {
            int r = 6 + i * 12;
            if (r + 12 > t.Length) break;
            if (U16(t, r + 6) != id) continue;
            int pid = U16(t, r), lid = U16(t, r + 4), len = U16(t, r + 8), off = U16(t, r + 10);
            int score = pid == 3 ? (lid == 0x409 ? 4 : 3) : pid == 0 ? 2 : pid == 1 && lid == 0 ? 1 : 0;
            if (score <= bestScore || strOff + off + len > t.Length) continue;
            var span = t.AsSpan(strOff + off, len);
            var s = (pid == 1 ? Encoding.Latin1.GetString(span) : Encoding.BigEndianUnicode.GetString(span)).Replace("\0", "").Trim();
            if (s.Length == 0) continue;
            best = s; bestScore = score;
        }
        return best;
    }

    private static int U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));
    private static int S16(byte[] d, int o) => BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(o, 2));
    private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
}
