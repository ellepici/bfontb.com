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

    public static Proposal ComputeProposal(IReadOnlyList<FontFile> files, IReadOnlyDictionary<string, FontInfo> analysis, Corrections fix)
    {
        var p = new Proposal();
        var recs = new List<(FontFile File, FontInfo Info, string Madre, string Variant, bool Guessed)>();
        foreach (var f in files)
        {
            if (fix.IsExcluded(f.Path) || !analysis.TryGetValue(f.Path, out var a)) continue;
            if (a.Error || a.Note is not null) { p.Unreadable.Add(f.Path); continue; }
            var (madre, variant, guessed) = StyleNames.Propose(a, f.FileName);
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
