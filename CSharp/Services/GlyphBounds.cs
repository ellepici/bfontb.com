using System.Buffers.Binary;
using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Dove sta il disegno di un glifo, letto dal file: il riquadro dell'inchiostro e la larghezza (avanzamento).
/// Serve per le forme alternative, che il canvas del browser non sa disegnare (non accende le funzioni OpenType)
/// e quindi non si possono misurare guardando i pixel: un accento per le maiuscole ('case'), per esempio,
/// ha larghezza zero e il disegno spostato di lato, e centrato sulla larghezza finirebbe fuori posto.
/// Formati: TrueType (glyf, il riquadro è scritto nel file) e CFF (lo si ricava seguendo il tracciato).
/// I CFF2 (alcuni font variabili OTF) non sono letti: si torna alla larghezza.
/// </summary>
public sealed class GlyphBounds
{
    private readonly int upm;
    private readonly byte[]? hmtx;
    private readonly int numHMetrics;
    private readonly Func<int, (int XMin, int YMin, int XMax, int YMax)?> outline;
    private readonly Dictionary<int, InkBox?> cache = [];

    private GlyphBounds(int upm, byte[]? hmtx, int numHMetrics, Func<int, (int, int, int, int)?> outline)
    {
        this.upm = upm;
        this.hmtx = hmtx;
        this.numHMetrics = numHMetrics;
        this.outline = outline;
    }

    /// <summary>Prepara la lettura; null se il file non ha né glyf né CFF leggibili.</summary>
    public static GlyphBounds? Create(Func<string, byte[]?> table)
    {
        try
        {
            var head = table("head");
            if (head is not { Length: >= 54 }) return null;
            int upm = U16(head, 18);
            if (upm is < 16 or > 16384) return null;
            var hhea = table("hhea");
            int numH = hhea is { Length: >= 36 } ? U16(hhea, 34) : 0;
            var hmtx = table("hmtx");

            var glyf = table("glyf");
            var loca = table("loca");
            if (glyf is not null && loca is not null)
            {
                bool longLoca = S16(head, 50) == 1;
                return new GlyphBounds(upm, hmtx, numH, g => GlyfBox(glyf, loca, longLoca, g));
            }
            var cff = table("CFF ");
            if (cff is not null && Cff.Open(cff) is { } font) return new GlyphBounds(upm, hmtx, numH, font.Box);
        }
        catch { /* file strano: niente misure, si usa la larghezza */ }
        return null;
    }

    /// <summary>Il disegno del glifo in em (null se non si sa leggere o il glifo è vuoto).</summary>
    public InkBox? Of(int glyph)
    {
        if (cache.TryGetValue(glyph, out var known)) return known;
        InkBox? box = null;
        try
        {
            if (outline(glyph) is { } b && b.XMax > b.XMin)
            {
                double u = upm;
                box = new InkBox(b.XMin / u, b.XMax / u, b.YMin / u, b.YMax / u, Advance(glyph) / u);
            }
        }
        catch { box = null; }
        if (cache.Count > 20_000) cache.Clear();
        cache[glyph] = box;
        return box;
    }

    private int Advance(int g)
    {
        if (hmtx is null || numHMetrics == 0) return 0;
        int i = Math.Min(g, numHMetrics - 1);
        return i * 4 + 2 <= hmtx.Length ? U16(hmtx, i * 4) : 0;
    }

    // ---- TrueType: il riquadro è nell'intestazione di ogni glifo ----

    private static (int, int, int, int)? GlyfBox(byte[] glyf, byte[] loca, bool longLoca, int g)
    {
        long start, end;
        if (longLoca)
        {
            if ((g + 2) * 4 > loca.Length) return null;
            start = U32(loca, g * 4); end = U32(loca, g * 4 + 4);
        }
        else
        {
            if ((g + 2) * 2 > loca.Length) return null;
            start = U16(loca, g * 2) * 2L; end = U16(loca, g * 2 + 2) * 2L;
        }
        if (end <= start || start + 10 > glyf.Length) return null;   // glifo vuoto (lo spazio)
        int o = (int)start;
        return (S16(glyf, o + 2), S16(glyf, o + 4), S16(glyf, o + 6), S16(glyf, o + 8));
    }

    // ---- CFF: si segue il tracciato (Type 2 charstring) e si prendono gli estremi dei punti ----
    // I punti di controllo delle curve sono compresi: nei font ben fatti gli estremi delle curve sono punti del tracciato,
    // quindi il riquadro coincide con quello del disegno.

    private sealed class Cff
    {
        private byte[] d = [];
        private List<(int Start, int End)> charStrings = [];
        private List<(int Start, int End)> globalSubrs = [];
        private List<List<(int Start, int End)>> localSubrs = [];   // uno per FD (uno solo nei font non CID)
        private Func<int, int> fdOf = _ => 0;

        public static Cff? Open(byte[] d)
        {
            if (d.Length < 4 || d[0] != 1) return null;   // solo CFF versione 1
            var c = new Cff { d = d };
            int p = d[2];
            p = Index(d, p, out _);                        // nomi
            p = Index(d, p, out var topDicts);             // Top DICT
            p = Index(d, p, out _);                        // stringhe
            Index(d, p, out c.globalSubrs);
            if (topDicts.Count == 0) return null;
            var top = Dict(d, topDicts[0].Start, topDicts[0].End);
            if (top.TryGetValue(1206, out var cst) && cst.Count > 0 && (int)cst[0] != 2) return null;   // CharstringType
            if (!top.TryGetValue(17, out var cs) || cs.Count == 0) return null;
            Index(d, (int)cs[0], out c.charStrings);
            if (top.TryGetValue(1236, out var fdArray) && fdArray.Count > 0)
            {
                // font CID: ogni glifo ha il suo dizionario privato (e i suoi sottoprogrammi)
                Index(d, (int)fdArray[0], out var fds);
                foreach (var (s, e) in fds) c.localSubrs.Add(c.PrivateSubrs(Dict(d, s, e)));
                if (top.TryGetValue(1237, out var fdSel) && fdSel.Count > 0) c.fdOf = c.FdSelect((int)fdSel[0]);
            }
            else c.localSubrs.Add(c.PrivateSubrs(top));
            return c;
        }

        private List<(int, int)> PrivateSubrs(Dictionary<int, List<double>> dict)
        {
            if (!dict.TryGetValue(18, out var priv) || priv.Count < 2) return [];
            int size = (int)priv[0], off = (int)priv[1];
            if (off <= 0 || off + size > d.Length) return [];
            var pd = Dict(d, off, off + size);
            if (!pd.TryGetValue(19, out var subrs) || subrs.Count == 0) return [];
            Index(d, off + (int)subrs[0], out var list);
            return list;
        }

        private Func<int, int> FdSelect(int o)
        {
            int fmt = d[o];
            if (fmt == 0) return g => o + 1 + g < d.Length ? d[o + 1 + g] : 0;
            if (fmt != 3) return _ => 0;
            int n = U16(d, o + 1);
            return g =>
            {
                for (int i = 0; i < n; i++)
                {
                    int r = o + 3 + i * 3;
                    int first = U16(d, r), next = U16(d, r + 3);
                    if (g >= first && g < next) return d[r + 2];
                }
                return 0;
            };
        }

        public (int, int, int, int)? Box(int g)
        {
            if (g < 0 || g >= charStrings.Count) return null;
            int fd = fdOf(g);
            var local = fd >= 0 && fd < localSubrs.Count ? localSubrs[fd] : [];
            var run = new Run(this, local);
            run.Exec(charStrings[g], 0);
            return run.Any ? ((int)Math.Floor(run.MinX), (int)Math.Floor(run.MinY), (int)Math.Ceiling(run.MaxX), (int)Math.Ceiling(run.MaxY)) : null;
        }

        private sealed class Run(Cff font, List<(int Start, int End)> local)
        {
            private readonly List<double> st = [];
            private int stems;
            private bool widthDone, ended;
            private double x, y;
            public double MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue;
            public bool Any;

            private void Pt(double px, double py)
            {
                Any = true;
                if (px < MinX) MinX = px; if (px > MaxX) MaxX = px;
                if (py < MinY) MinY = py; if (py > MaxY) MaxY = py;
            }

            // il primo operatore che svuota la pila può portare davanti la larghezza del glifo: la si salta
            private int Width(bool hasExtra) { int skip = !widthDone && hasExtra ? 1 : 0; widthDone = true; return skip; }

            private void Line(double dx, double dy) { Pt(x, y); x += dx; y += dy; Pt(x, y); }
            private void Curve(double a, double b, double c, double e, double f, double h)
            {
                Pt(x, y);
                double x1 = x + a, y1 = y + b, x2 = x1 + c, y2 = y1 + e;
                x = x2 + f; y = y2 + h;
                Pt(x1, y1); Pt(x2, y2); Pt(x, y);
            }

            private static int Bias(int n) => n < 1240 ? 107 : n < 33900 ? 1131 : 32768;

            public void Exec((int Start, int End) cs, int depth)
            {
                if (depth > 10) return;
                var d = font.d;
                int p = cs.Start, end = Math.Min(cs.End, d.Length);
                while (p < end && !ended)
                {
                    int b0 = d[p++];
                    if (b0 >= 32 || b0 == 28)
                    {
                        if (b0 == 28) { st.Add((short)(d[p] << 8 | d[p + 1])); p += 2; }
                        else if (b0 <= 246) st.Add(b0 - 139);
                        else if (b0 <= 250) st.Add((b0 - 247) * 256 + d[p++] + 108);
                        else if (b0 <= 254) st.Add(-(b0 - 251) * 256 - d[p++] - 108);
                        else { st.Add(BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(p, 4)) / 65536.0); p += 4; }
                        if (st.Count > 96) return;   // pila impazzita: file danneggiato
                        continue;
                    }
                    int n = st.Count, i;
                    switch (b0)
                    {
                        case 1: case 3: case 18: case 23:   // hstem vstem hstemhm vstemhm
                            stems += (n - Width(n % 2 == 1)) / 2; st.Clear(); break;
                        case 19: case 20:                   // hintmask cntrmask (eventuali vstem sottintesi)
                            stems += (n - Width(n % 2 == 1)) / 2; st.Clear(); p += (stems + 7) / 8; break;
                        case 21: i = Width(n > 2); if (n - i >= 2) { x += st[i]; y += st[i + 1]; } st.Clear(); break;   // rmoveto
                        case 22: i = Width(n > 1); if (n - i >= 1) x += st[i]; st.Clear(); break;                       // hmoveto
                        case 4: i = Width(n > 1); if (n - i >= 1) y += st[i]; st.Clear(); break;                        // vmoveto
                        case 5: for (i = 0; i + 1 < n; i += 2) Line(st[i], st[i + 1]); st.Clear(); break;              // rlineto
                        case 6: case 7:                                                                                 // hlineto vlineto
                        {
                            bool h = b0 == 6;
                            for (i = 0; i < n; i++, h = !h) { if (h) Line(st[i], 0); else Line(0, st[i]); }
                            st.Clear(); break;
                        }
                        case 8: for (i = 0; i + 5 < n; i += 6) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]); st.Clear(); break;
                        case 24:                                                                                        // rcurveline
                            for (i = 0; i + 5 < n - 2; i += 6) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                            if (i + 1 < n) Line(st[i], st[i + 1]);
                            st.Clear(); break;
                        case 25:                                                                                        // rlinecurve
                            for (i = 0; i + 1 < n - 6; i += 2) Line(st[i], st[i + 1]);
                            if (i + 5 < n) Curve(st[i], st[i + 1], st[i + 2], st[i + 3], st[i + 4], st[i + 5]);
                            st.Clear(); break;
                        case 26:                                                                                        // vvcurveto
                        {
                            i = 0; double dx1 = 0;
                            if (n % 2 == 1) { dx1 = st[0]; i = 1; }
                            for (; i + 3 < n; i += 4) { Curve(dx1, st[i], st[i + 1], st[i + 2], 0, st[i + 3]); dx1 = 0; }
                            st.Clear(); break;
                        }
                        case 27:                                                                                        // hhcurveto
                        {
                            i = 0; double dy1 = 0;
                            if (n % 2 == 1) { dy1 = st[0]; i = 1; }
                            for (; i + 3 < n; i += 4) { Curve(st[i], dy1, st[i + 1], st[i + 2], st[i + 3], 0); dy1 = 0; }
                            st.Clear(); break;
                        }
                        case 30: case 31:                                                                               // vhcurveto hvcurveto
                        {
                            bool h = b0 == 31;
                            for (i = 0; i + 3 < n; i += 4, h = !h)
                            {
                                double last = n - i == 5 ? st[i + 4] : 0;
                                if (h) Curve(st[i], 0, st[i + 1], st[i + 2], last, st[i + 3]);
                                else Curve(0, st[i], st[i + 1], st[i + 2], st[i + 3], last);
                            }
                            st.Clear(); break;
                        }
                        case 10: case 29:                                                                               // callsubr callgsubr
                        {
                            if (n == 0) return;
                            var subrs = b0 == 10 ? local : font.globalSubrs;
                            int k = (int)st[n - 1] + Bias(subrs.Count);
                            st.RemoveAt(n - 1);
                            if (k >= 0 && k < subrs.Count) Exec(subrs[k], depth + 1);
                            break;
                        }
                        case 11: return;                                                                                // return
                        case 14: ended = true; return;                                                                  // endchar
                        case 12:
                        {
                            int b1 = p < end ? d[p++] : 0;
                            double x0 = x, y0 = y;
                            switch (b1)
                            {
                                case 35 when n >= 12:   // flex
                                    Curve(st[0], st[1], st[2], st[3], st[4], st[5]); Curve(st[6], st[7], st[8], st[9], st[10], st[11]); break;
                                case 34 when n >= 7:    // hflex
                                    Curve(st[0], 0, st[1], st[2], st[3], 0); Curve(st[4], 0, st[5], -st[2], st[6], 0); break;
                                case 36 when n >= 9:    // hflex1
                                    Curve(st[0], st[1], st[2], st[3], st[4], 0);
                                    Curve(st[5], 0, st[6], st[7], st[8], y0 - (y + st[7])); break;
                                case 37 when n >= 11:   // flex1
                                {
                                    double sx = st[0] + st[2] + st[4] + st[6] + st[8], sy = st[1] + st[3] + st[5] + st[7] + st[9];
                                    Curve(st[0], st[1], st[2], st[3], st[4], st[5]);
                                    if (Math.Abs(sx) > Math.Abs(sy)) Curve(st[6], st[7], st[8], st[9], st[10], y0 - (y + st[7] + st[9]));
                                    else Curve(st[6], st[7], st[8], st[9], x0 - (x + st[6] + st[8]), st[10]);
                                    break;
                                }
                            }
                            st.Clear(); break;   // le operazioni aritmetiche (rarissime) non servono al riquadro
                        }
                        default: st.Clear(); break;
                    }
                }
            }
        }

        // ---- strutture del CFF ----

        private static int Index(byte[] d, int p, out List<(int Start, int End)> items)
        {
            items = [];
            if (p < 0 || p + 2 > d.Length) return d.Length;
            int count = U16(d, p);
            if (count == 0) return p + 2;
            int offSize = d[p + 2];
            if (offSize is < 1 or > 4) return d.Length;
            int offs = p + 3, dataStart = offs + (count + 1) * offSize - 1;
            int Off(int i) { int v = 0; for (int k = 0; k < offSize; k++) v = v << 8 | d[offs + i * offSize + k]; return v; }
            if (offs + (count + 1) * offSize > d.Length) return d.Length;
            for (int i = 0; i < count; i++) items.Add((dataStart + Off(i), dataStart + Off(i + 1)));
            return dataStart + Off(count);
        }

        /// <summary>Un DICT: operatore → operandi (gli operatori a due byte come 1200 + secondo byte).</summary>
        private static Dictionary<int, List<double>> Dict(byte[] d, int p, int end)
        {
            var res = new Dictionary<int, List<double>>();
            var ops = new List<double>();
            end = Math.Min(end, d.Length);
            while (p < end)
            {
                int b0 = d[p++];
                if (b0 <= 21)
                {
                    int op = b0 == 12 && p < end ? 1200 + d[p++] : b0;
                    res[op] = ops;
                    ops = [];
                }
                else if (b0 == 28) { ops.Add((short)(d[p] << 8 | d[p + 1])); p += 2; }
                else if (b0 == 29) { ops.Add(BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(p, 4))); p += 4; }
                else if (b0 == 30) { while (p < end) { int nb = d[p++]; if ((nb & 0x0F) == 0x0F || (nb >> 4) == 0x0F) break; } ops.Add(0); }   // reale: non serve
                else if (b0 >= 32 && b0 <= 246) ops.Add(b0 - 139);
                else if (b0 >= 247 && b0 <= 250) ops.Add((b0 - 247) * 256 + d[p++] + 108);
                else if (b0 >= 251 && b0 <= 254) ops.Add(-(b0 - 251) * 256 - d[p++] - 108);
            }
            return res;
        }
    }

    private static int U16(byte[] b, int o) => b[o] << 8 | b[o + 1];
    private static int S16(byte[] b, int o) => (short)(b[o] << 8 | b[o + 1]);
    private static long U32(byte[] b, int o) => (long)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
}
