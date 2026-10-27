using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

public partial class FamilyView
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    // i salti alle varianti si fanno a mano: con <base href="/"> un link "#…" porterebbe all'indirizzo "/" (la presentazione)
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    private sealed record VariantGroup(string Name, string Anchor, List<FontStyle> Styles);

    private TypeFamily? Current;
    private List<VariantGroup> Variants = [];
    private string Summary = "";
    private List<string> Traits = [];

    /// <summary>Con un filtro attivo si vedono solo le varianti che rispondono; l'utente può chiedere di vederle tutte.</summary>
    private bool showAll;
    private string? showAllFor;
    private int HiddenVariants;
    private string FilterLabel = "";

    protected override void OnParametersSet() => Compute();

    // arrivati dal nome di una variante nel catalogo (durante una ricerca): si scende alla sua sezione
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Lib.JumpTo is not { } id) return;
        Lib.JumpTo = null;
        try { await Bridge.ScrollToSoonAsync(id); } catch { /* pagina cambiata */ }
    }

    private void Compute()
    {
        Current = (Lib.CurrentFamily is not null ? Lib.Families.GetValueOrDefault(Lib.CurrentFamily) : null)
                  ?? Lib.VisibleFamilies().FirstOrDefault();
        if (Current is null) return;
        Lib.CurrentFamily = Current.Name;
        if (showAllFor != Current.Name) { showAll = false; showAllFor = Current.Name; }
        var names = Current.VariantNames(Lib.IncludeWeb).ToList();
        HiddenVariants = 0;
        FilterLabel = string.Join(" ", FontClassifier.All.Where(a => Lib.Filters.HasFlag(a.Trait)).Select(a => a.Label));
        if (Lib.Filters != FontTrait.None && !showAll)
        {
            var matching = Current.Matching(Lib.Filters).Select(x => x.Variant).ToHashSet();
            if (matching.Count > 0)
            {
                HiddenVariants = names.Count(n => !matching.Contains(n));
                names = names.Where(matching.Contains).ToList();
            }
        }
        Variants = names.Select(v => new VariantGroup(v, FontLibrary.VariantAnchor(v), Sort(Current.Variants[v]))).ToList();
        var traits = Current.Traits();
        Traits = FontClassifier.All.Where(a => traits.HasFlag(a.Trait)).Select(a => a.Label).ToList();
        int count = Current.Count(Lib.IncludeWeb);
        Summary = $"{count} {Fmt.Plural(count, "style", "styles")} · {Variants.Count} {Fmt.Plural(Variants.Count, "variant", "variants")}";
    }

    private void ShowAll()
    {
        showAll = true;
        Compute();
    }

    private string HiddenText => HiddenVariants == 1 ? "1 other variant is hidden." : $"{HiddenVariants} other variants are hidden.";

    private static List<FontStyle> Sort(IEnumerable<FontStyle> list) =>
        list.OrderBy(s => s.Weight).ThenBy(s => s.Italic).ThenBy(s => s.FileName, StringComparer.Ordinal).ToList();

    // con i nomi tedeschi degli stili, accanto al peso il nome inglese ("Fett" · weight ~800 · ExtraBold)
    private static string WeightNote(FontStyle s) =>
        $"weight ~{s.Weight}" + (StyleNames.EnglishWeight(s.Label, s.Weight) is { } en ? $" · {en}" : "") + (s.Italic ? " · italic" : "");

    private static string CardNote(FontStyle s) =>
        $"~{s.Weight}" + (StyleNames.EnglishWeight(s.Label, s.Weight) is { } en ? $" · {en}" : "") + (s.Italic ? " · italic" : "");

    // ---- griglia: schede grandi come quelle del catalogo (crescono con il corpo scelto in alto) ----
    private const int Gap = 16;
    private int CardWidth => 104 + (int)Math.Round(Lib.Size * 1.5);
    private int TileHeight => (int)Math.Round(CardWidth * 0.8);
    private double AaSize => Math.Round(CardWidth * 0.36, 1);
    private string GridStyle => $"--card-w:{CardWidth}px;--gap:{Gap}px;--tile-h:{TileHeight}px";
}
