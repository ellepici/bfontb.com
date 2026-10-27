using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

public partial class PrepView
{
    [Inject] private FontLibrary Lib { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }
    [Inject] private AnalysisRunner Runner { get; set; } = default!;

    private const int ListLimit = 400;

    /// <summary>Una cartella (o sottocartella) che si può escludere.</summary>
    public sealed record FolderRow(string Name, string FullPath, int Count, bool IsSub, string Id);

    private sealed record FolderSummary(int Fonts, int Web, int Loose, bool Ready, List<FolderRow> TopFolders);

    /// <summary>Una famiglia madre della proposta con le sue varianti.</summary>
    public sealed record MadreRow(string Name, List<VariantRow> Variants)
    {
        public int Total => Variants.Sum(v => v.Count);
    }

    /// <summary>Una variante della proposta; Keys sono le chiavi da usare per spostarla.</summary>
    public sealed record VariantRow(string Name, int Count, int Guessed, HashSet<string> Keys);

    private FolderSummary Folder = new(0, 0, 0, true, []);
    private List<MadreRow> Madri = [];
    private List<MadreRow> ShownMadri = [];
    private int VariantCount;
    private int Analyzable, Analyzed;
    private CancellationTokenSource? filterDelay;

    private bool AnalysisComplete => Analyzable > 0 && Analyzed >= Analyzable;

    private int ProgressPercent => Runner.Running ? Runner.Percent : Analyzable == 0 ? 0 : (int)Math.Round(Analyzed * 100.0 / Analyzable);

    private string ProgressText => Runner.Running
        ? $"{Fmt.N(Runner.Done)} of {Fmt.N(Runner.Total)} files read ({Runner.Percent}%) · files that are only in iCloud are downloaded as they’re read"
        : $"{Fmt.N(Analyzed)} of {Fmt.N(Analyzable)} files read";

    private string AnalysisText =>
        Runner.Running ? "Reading the files. You can keep using the other tabs."
        : AnalysisComplete ? $"All {Fmt.N(Analyzable)} files have been read. The results stay saved in this browser, so next time the folder opens already sorted."
        : Analyzed > 0 ? $"{Fmt.N(Analyzed)} of {Fmt.N(Analyzable)} files read. Resume to finish."
        : $"To read: {Fmt.N(Analyzable)} files. If the files are only in iCloud, the first read downloads them all, so it can take time and disk space.";

    protected override void OnParametersSet()
    {
        if (Lib.IsDemo) return;
        Folder = SummarizeFolder(Lib.Files, Lib.RootName);
        var toAnalyze = Lib.FilesToAnalyze().ToList();
        Analyzable = toAnalyze.Count;
        Analyzed = toAnalyze.Count(f => !Lib.NeedsAnalysis(f));
        BuildMadri();
    }

    private static FolderSummary SummarizeFolder(List<FontFile> files, string root)
    {
        int fonts = files.Count(f => f.IsDesktop);
        int loose = files.Count(f => f.Segments.Length < 3);
        var tops = files.Where(f => f.Segments.Length >= 3).GroupBy(f => f.Segments[1])
            .OrderBy(g => g.Key, StringComparer.InvariantCultureIgnoreCase).ToList();
        var subs = files.Where(f => f.Segments.Length >= 4).GroupBy(f => f.Segments[1] + "/" + f.Segments[2])
            .ToDictionary(g => g.Key, g => g.Count());
        bool showSubs = tops.Count <= 60;
        var rows = new List<FolderRow>();
        foreach (var t in tops)
        {
            rows.Add(new FolderRow(t.Key, $"{root}/{t.Key}", t.Count(), false, Fmt.Id("ex-", t.Key)));
            if (!showSubs) continue;
            foreach (var (k, n) in subs.Where(s => s.Key.StartsWith(t.Key + "/", StringComparison.Ordinal)).OrderBy(s => s.Key, StringComparer.Ordinal))
                rows.Add(new FolderRow(k[(k.IndexOf('/') + 1)..], $"{root}/{k}", n, true, Fmt.Id("ex-", k)));
        }
        return new FolderSummary(fonts, files.Count - fonts, loose, FamilyOrganizer.StructureLooksReady(files), rows);
    }

    private void BuildMadri()
    {
        Madri = [];
        VariantCount = 0;
        if (Lib.Proposal is null) { ShownMadri = []; return; }
        var map = new Dictionary<string, Dictionary<string, (int Count, int Guessed, HashSet<string> Keys)>>();
        foreach (var p in Lib.Proposal.ByPath.Values)
        {
            if (p.Duplicate) continue;
            if (!map.TryGetValue(p.Madre, out var vars)) map[p.Madre] = vars = [];
            var cur = vars.TryGetValue(p.Variant, out var c) ? c : (Count: 0, Guessed: 0, Keys: new HashSet<string>());
            cur.Keys.Add(p.AutoMadre + "::" + p.AutoVariant);
            vars[p.Variant] = (cur.Count + 1, cur.Guessed + (p.Guessed ? 1 : 0), cur.Keys);
        }
        Madri = map.OrderBy(m => m.Key, StringComparer.InvariantCultureIgnoreCase)
            .Select(m => new MadreRow(m.Key, m.Value.OrderBy(v => v.Key, StringComparer.InvariantCultureIgnoreCase)
                .Select(v => new VariantRow(v.Key, v.Value.Count, v.Value.Guessed, v.Value.Keys)).ToList()))
            .ToList();
        VariantCount = Madri.Sum(m => m.Variants.Count(v => v.Name != "Web"));
        var q = Lib.PropQuery.Trim();
        ShownMadri = q.Length == 0 ? Madri : Madri.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || m.Variants.Any(v => v.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private Task StartAnalysis() => Runner.RunAsync();

    private Task OnAnalyzeWeb(ChangeEventArgs e) => Lib.SetAnalyzeWebAsync(e.Value is true);

    private async Task ResetCorrections()
    {
        Lib.Fix.Rename.Clear();
        Lib.Fix.Merge.Clear();
        Lib.Fix.Move.Clear();
        await Lib.ApplyChangesAsync();
    }

    private async Task OnFilterInput(ChangeEventArgs e)
    {
        filterDelay?.Cancel();
        filterDelay = new CancellationTokenSource();
        var token = filterDelay.Token;
        try { await Task.Delay(250, token); } catch (TaskCanceledException) { return; }
        Lib.PropQuery = e.Value?.ToString() ?? "";
        Lib.Notify();
    }

    private static string MoreNote(int total) => total > ListLimit ? $"… and {Fmt.N(total - ListLimit)} more" : "";
}
