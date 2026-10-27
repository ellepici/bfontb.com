using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Costruisce l'elenco delle famiglie: dalle cartelle così come sono, oppure dalla proposta dell'analisi
/// (con le correzioni dell'utente). Si occupa anche di riconoscere i doppioni.
/// </summary>
public static class FamilyOrganizer
{
    /// <summary>La cartella è già divisa per famiglia? (la maggior parte dei font sta in sottocartelle)</summary>
    public static bool StructureLooksReady(IReadOnlyList<FontFile> files)
    {
        var fonts = files.Where(f => f.IsDesktop).ToList();
        if (fonts.Count == 0) return true;
        double inSub = fonts.Count(f => f.Segments.Length >= 3) / (double)fonts.Count;
        int tops = fonts.Select(f => f.Segments[1]).Distinct().Count();
        return inSub > 0.8 && tops > 1;
    }

    /// <summary>Cartella/Madre/[Variante]/file → famiglia e variante. File sciolti: famiglia dal nome del file.</summary>
    public static (string Family, string Variant)? FromFolders(FontFile f)
    {
        var rel = f.Segments.Skip(1).ToArray();
        string fam, variant;
        if (rel.Length < 2) { fam = StyleNames.GuessFamily(f.FileName); variant = fam; }
        else { fam = rel[0]; variant = rel.Length >= 3 ? rel[1] : fam; }
        return variant == "Extra" ? null : (fam, variant);
    }

    /// <summary>
    /// Se il file sta nella cartella di una famiglia e il nome scritto dentro il font è la stessa famiglia scritta in un
    /// altro modo, vale il nome della cartella. Succede con i refusi del produttore ("ABC Asflat" nella cartella
    /// "ABC Asfalt"), con gli accenti ("Landnáma"), con le sigle della fonderia ("ITC Cheltenham Std" nella cartella
    /// "Cheltenham", "Favorit Mono" in "ABC Favorit") e con i nomi accorciati ("NimbusSanL" in "Nimbus Sans").
    /// La variante segue la sottocartella, se c'è. Un nome del tutto diverso dalla cartella resta quello del font.
    /// </summary>
    public static (string Madre, string Variant) PreferFolder(FontFile f, string madre, string variant)
    {
        if (f.Segments.Length < 3) return (madre, variant);   // file sciolto, fuori da ogni cartella di famiglia
        var folder = f.Segments[1];
        if (folder == madre || !SameFamily(madre, folder)) return (madre, variant);
        string v;
        if (f.Segments.Length >= 4 && f.Segments[2] is not ("Web" or "Extra")) v = f.Segments[2];
        else
        {
            var rest = variant.StartsWith(madre, StringComparison.OrdinalIgnoreCase) ? variant[madre.Length..].Trim() : "";
            v = rest.Length == 0 ? folder : folder + " " + rest;
        }
        return (folder, v);
    }

    /// <summary>Lo stesso nome di famiglia, a meno di refusi, accenti, sigle della fonderia e diciture come Std o Trial?</summary>
    public static bool SameFamily(string fontName, string folder)
    {
        var a = Key(fontName);
        if (a.Length == 0) return false;
        // tutte le parole della cartella ci sono nel nome del font, anche in un altro ordine:
        // "Neue Faktum" e "RB Faktum Neue Condensed", "Azo" e "Azo Sans", "ABC ROM" e "ABC ROM Mono"
        var fontWords = Words(fontName);
        var folderWords = Words(folder);
        if (folderWords.Count > 0 && folderWords.All(fontWords.Contains)) return true;
        // la cartella senza la sigla della fonderia davanti: "ABC Favorit" → "favorit", "PP Neue Montreal" → "neuemontreal"
        var words = folder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = Key(folder);
        var core = words.Length > 1 && words[0].Length <= 3 ? Key(string.Join(' ', words.Skip(1))) : b;
        foreach (var c in core == b ? [b] : new[] { b, core })
        {
            if (c.Length < 4 || a.Length < 4) continue;
            if (a.Contains(c) || c.Contains(a)) return true;
            // un refuso (una lettera scambiata, mancante o in più) nella parte iniziale del nome: "Asflat" per "Asfalt"
            if (c.Length >= 5 && Distance(a[..Math.Min(a.Length, c.Length)], c) <= 1) return true;
        }
        return a == b;
    }

    private static HashSet<string> Words(string s) =>
        NoiseWord.Replace(System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2"), " ")
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(Key).Where(w => w.Length > 0)
            .Select(w => WordAlias.GetValueOrDefault(w, w)).ToHashSet();

    // nomi tedeschi delle famiglie che nella cartella si chiamano in inglese: "AG Buch" è "AG Book"
    private static readonly Dictionary<string, string> WordAlias = new() { ["buch"] = "book" };

    private static readonly System.Text.RegularExpressions.Regex NoiseWord =
        new(@"\b(unlicensed|trial|test|demo|webfont|web|std|pro|lt|mt|bt|variable|vf|var)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // minuscole, senza accenti, senza spazi né segni, senza le diciture che non fanno parte del nome
    private static string Key(string s)
    {
        s = NoiseWord.Replace(System.Text.RegularExpressions.Regex.Replace(s, "([a-z])([A-Z])", "$1 $2"), " ");
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // distanza di Damerau-Levenshtein ristretta (anche lo scambio di due lettere vicine conta uno)
    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int c = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + c);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    public static Proposal ComputeProposal(IReadOnlyList<FontFile> files, IReadOnlyDictionary<string, FontInfo> analysis, Corrections fix)
    {
        var p = new Proposal();
        var recs = new List<(FontFile File, FontInfo Info, string Madre, string Variant, bool Guessed)>();
        foreach (var f in files)
        {
            if (fix.IsExcluded(f.Path) || !analysis.TryGetValue(f.Path, out var a)) continue;
            if (a.Error || a.Note is not null) { p.Unreadable.Add(f.Path); continue; }
            var (madre, variant, guessed) = StyleNames.Propose(a, f.FileName);
            (madre, variant) = PreferFolder(f, madre, variant);
            if (guessed) p.Guessed.Add(f.Path);
            recs.Add((f, a, madre, variant, guessed));
        }

        // Superfamiglie: "Lyon Text" e "Lyon Display" sotto "Lyon"; "ABC Diatype Mono" sotto "ABC Diatype".
        var names = recs.Select(r => r.Madre).Distinct().ToList();
        var byLower = names.GroupBy(n => n.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var parent = new Dictionary<string, string>();
        foreach (var n in names)
        {
            var w = n.Split(' ');
            for (int len = w.Length - 1; len >= 1; len--)
            {
                if (!byLower.TryGetValue(string.Join(' ', w.Take(len)).ToLowerInvariant(), out var cand)) continue;
                if (len == 1 && cand.Length < 5) continue;   // evita "The", "New", "Neue"…
                parent[n] = cand;
                break;
            }
        }
        string Top(string n) { for (int k = 0; k < 10 && parent.TryGetValue(n, out var up); k++) n = up; return n; }
        string Final(string auto, string variant)
        {
            var m = fix.Move.TryGetValue(auto + "::" + variant, out var moved) ? moved : Top(auto);
            for (int k = 0; k < 20; k++)
            {
                var next = fix.Merge.GetValueOrDefault(m) ?? fix.Rename.GetValueOrDefault(m);
                if (next is null || next == m) break;
                m = next;
            }
            return m;
        }

        var seen = new Dictionary<string, string>();
        foreach (var r in recs.OrderBy(r => r.File.Path.Length).ThenBy(r => r.File.Path, StringComparer.Ordinal))
        {
            var madre = Final(r.Madre, r.Variant);
            var variant = r.File.IsWeb ? "Web" : r.Variant;
            var ext = r.File.FileName[(r.File.FileName.LastIndexOf('.') + 1)..];
            var key = string.Join('|', madre, variant, r.Info.Full ?? r.Info.Sub, r.Info.Version, r.File.Size, ext).ToLowerInvariant();
            if (seen.TryGetValue(key, out var kept))
            {
                p.Duplicates.Add((kept, r.File.Path));
                p.ByPath[r.File.Path] = new Placement(madre, variant, true, r.Madre, r.Variant, r.Guessed);
                continue;
            }
            seen[key] = r.File.Path;
            p.ByPath[r.File.Path] = new Placement(madre, variant, false, r.Madre, r.Variant, r.Guessed);
        }
        return p;
    }

    /// <summary>Crea le famiglie da mostrare nel catalogo.</summary>
    public static Dictionary<string, TypeFamily> Build(IReadOnlyList<FontFile> files, OrgMode mode, Proposal? proposal,
        IReadOnlyDictionary<string, FontInfo> analysis, Corrections fix)
    {
        var families = new Dictionary<string, TypeFamily>(StringComparer.Ordinal);
        void Put(string fam, string variant, FontStyle st)
        {
            if (!families.TryGetValue(fam, out var tf)) families[fam] = tf = new TypeFamily(fam);
            tf.Add(variant, st);
        }

        foreach (var f in files)
        {
            if (fix.IsExcluded(f.Path)) continue;
            if (mode == OrgMode.Analysis && proposal is not null)
            {
                if (!proposal.ByPath.TryGetValue(f.Path, out var pl) || pl.Duplicate) continue;
                var a = analysis[f.Path];
                var label = a.Sub ?? StyleNames.StyleLabel(f.FileName);
                Put(pl.Madre, pl.Variant, new FontStyle
                {
                    File = f, Path = f.Path, FileName = f.FileName, IsWeb = f.IsWeb, Info = a, Label = label,
                    Weight = a.WeightClass ?? StyleNames.WeightOf(label),
                    Italic = a.FsSelection is not null ? a.IsItalicFlag || StyleNames.IsItalic(label) : StyleNames.IsItalic(label),
                });
            }
            else if (FromFolders(f) is var (fam, variant))
            {
                var label = StyleNames.StyleLabel(f.FileName);
                Put(fam, variant, new FontStyle
                {
                    File = f, Path = f.Path, FileName = f.FileName, IsWeb = f.IsWeb, Label = label,
                    Weight = StyleNames.WeightOf(label), Italic = StyleNames.IsItalic(label),
                });
            }
        }
        return families;
    }
}
