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
    /// La variante segue la sottocartella, se c'è, a meno che il font dica di essere di un'altra larghezza o sottofamiglia:
    /// un "Ultra Expanded Hairline" finito nella sottocartella "Antarctica" (quella normale) va in "Antarctica UltraExpanded".
    /// Un nome del tutto diverso dalla cartella resta quello del font.
    /// </summary>
    public static (string Madre, string Variant) PreferFolder(FontFile f, string madre, string variant)
    {
        if (f.Segments.Length < 3) return (madre, variant);   // file sciolto, fuori da ogni cartella di famiglia
        var folder = f.Segments[1];
        if (folder == madre || !SameFamily(madre, folder)) return (madre, variant);
        // quello che il font aggiunge alla famiglia ("UltraExpanded", "Mono", "Condensed"…), senza le parole che il nome
        // della cartella ha già: "General Sans" con il font "General" + "Sans" non diventa "General Sans Sans"
        var have = Words(folder);
        var rest = string.Join(' ', (variant.StartsWith(madre, StringComparison.OrdinalIgnoreCase) ? variant[madre.Length..] : "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !have.Contains(WordAlias.GetValueOrDefault(Key(w), Key(w)))));
        var own = rest.Length == 0 ? folder : folder + " " + rest;
        if (f.Segments.Length >= 4 && f.Segments[2] is not ("Web" or "Extra"))
            return (folder, SubfolderAgrees(f.Segments[2], folder, rest) ? f.Segments[2] : own);
        return (folder, own);
    }

    /// <summary>
    /// La sottocartella dice la stessa variante del font? "Antarctica XCondensed" e "ExtraCondensed" sì,
    /// "Antarctica Condensed Extended" e "Condensed" sì (la cartella è più precisa), "Antarctica" e "UltraExpanded" no.
    /// Se il font non dice nessuna variante, vale sempre la sottocartella (è come l'ha ordinata chi la usa).
    /// </summary>
    private static bool SubfolderAgrees(string subfolder, string folder, string rest)
    {
        var r = VariantKey(rest);
        if (r.Length == 0) return true;
        var sub = VariantKey(subfolder);
        var fam = VariantKey(folder);
        if (sub.StartsWith(fam, StringComparison.Ordinal)) sub = sub[fam.Length..];
        return sub.Contains(r, StringComparison.Ordinal);
    }

    // le parole di larghezza scritte in modi diversi diventano uguali: XCondensed = ExtraCondensed, Cond = Condensed
    private static string VariantKey(string s)
    {
        var k = Key(s);
        k = System.Text.RegularExpressions.Regex.Replace(k, "x(?=(condensed|compressed|narrow|expanded|extended|wide))", "extra");
        k = System.Text.RegularExpressions.Regex.Replace(k, "cond(?!ensed)|cn(?=$|[^a-z])", "condensed");
        return k;
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
            if (mode == OrgMode.Analysis && proposal is not null && proposal.ByPath.ContainsKey(f.Path))
            {
                var pl = proposal.ByPath[f.Path];
                if (pl.Duplicate) continue;
                var a = analysis[f.Path];
                var sub = a.Sub ?? StyleNames.StyleLabel(f.FileName);
                bool italic = a.FsSelection is not null ? a.IsItalicFlag || StyleNames.IsItalic(sub) : StyleNames.IsItalic(sub);
                // file con lo stile scritto sbagliato ("Regular" in un Bold): vale il nome completo, se il peso del file lo conferma
                if (!a.IsVariable) sub = StyleNames.TrueSub(sub, a.Family, a.Full, a.WeightClass, italic, f.FileName);
                // un font variabile non è "Regular" o "Compressed Light": quello è solo il punto da cui parte, il peso lo
                // sceglie chi lo usa. Si chiama quindi "Variable" (o "Variable Italic"), come nei nomi dei file
                var label = a.IsVariable ? (italic ? "Variable Italic" : "Variable") : sub;
                Put(pl.Madre, pl.Variant, new FontStyle
                {
                    File = f, Path = f.Path, FileName = f.FileName, IsWeb = f.IsWeb, Info = a, Label = label,
                    Weight = StyleNames.ReconcileWeight(a.WeightClass, sub),
                    Italic = italic,
                });
            }
            else if ((mode != OrgMode.Analysis || proposal is null || f.IsWeb) && FromFolders(f) is var (fam, variant))
            {
                // i font per il web (woff, woff2) stanno sempre nella variante "Web" della loro famiglia: anche quando l'analisi
                // non li ha letti (si leggono solo se lo si chiede), così il filtro Web li trova
                var label = StyleNames.StyleLabel(f.FileName);
                Put(fam, f.IsWeb ? "Web" : variant, new FontStyle
                {
                    File = f, Path = f.Path, FileName = f.FileName, IsWeb = f.IsWeb, Label = label,
                    Weight = StyleNames.WeightOf(label), Italic = StyleNames.IsItalic(label),
                });
            }
        }
        return families;
    }
}
