using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>Il percorso in cima alla colonna dei caratteri: dice sempre dove ci si trova e permette di risalire.</summary>
public partial class Breadcrumbs
{
    [Inject] private FontLibrary Lib { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    /// <summary>Un passo del percorso. Counts: accanto c'è il conteggio animato di famiglie e stili (solo il catalogo).</summary>
    private sealed record Crumb(string Text, Func<Task>? Go = null, string? Note = null, bool Counts = false);

    // famiglie e stili che rispondono a filtri e ricerca (numeri animati con NumberFlow, vedi FlowNumber)
    private int CountFamilies, CountStyles;

    private List<Crumb> Crumbs = [];

    protected override void OnParametersSet()
    {
        var list = new List<Crumb>();
        var catalog = new Crumb("Catalog", () => { Lib.ShowCatalog(); return Task.CompletedTask; });
        switch (Lib.IsDemo && !Lib.IsSidePage ? View.Catalog : Lib.View)
        {
            case View.Catalog:
                list.Add(catalog with { Go = null, Note = CatalogNote(), Counts = Lib.StatusMessage is null && !Lib.IsDemo });
                break;
            // nella famiglia e nello stile al catalogo riporta già il pulsante "Back to the catalog": il percorso parte dalla famiglia
            case View.Family:
                // come la pagina della famiglia: se non ne è stata scelta una, la prima dell'elenco;
                // accanto, quante varianti, quanti stili e quanti glifi ha (quelli del suo file più completo)
                {
                    var name = Lib.CurrentFamily ?? Lib.VisibleFamilies().FirstOrDefault()?.Name;
                    var fam = name is null ? null : Lib.Families.GetValueOrDefault(name);
                    list.Add(new Crumb(name ?? "Family", Note: fam is null ? null : FamilyNote(fam)));
                }
                break;
            case View.Style when Lib.CurrentStyle is { } r:
                list.Add(new Crumb(r.Family.Name, () => { Lib.OpenFamily(r.Family.Name); return Task.CompletedTask; }));
                if (r.Variant != r.Family.Name) list.Add(new Crumb(r.Variant));
                list.Add(new Crumb(r.Style.Label));
                break;
            case View.Style:
                list.Add(new Crumb("Style"));
                break;
            case View.Settings:
                list.Add(new Crumb("Settings"));
                break;
        }
        Crumbs = list;
    }

    /// <summary>
    /// Nel catalogo (l'unico posto dove compaiono): quante famiglie e quanti stili si vedono, con quali filtri o ricerca;
    /// mentre si apre una cartella, il messaggio di stato.
    /// </summary>
    private string? CatalogNote()
    {
        if (Lib.StatusMessage is { } status) return status;
        if (Lib.IsDemo) return "No folder open";
        // i due numeri li scrive il percorso (animati); qui resta il resto: filtri e ricerca
        var parts = new List<string>();
        var families = Lib.VisibleFamilies().ToList();
        CountFamilies = families.Count;
        CountStyles = families.Sum(f => f.Count(false));
        var filters = FontClassifier.All.Where(a => Lib.Filters.HasFlag(a.Trait)).Select(a => a.Label).ToList();
        if (filters.Count > 0) parts.Add("filter " + string.Join(" + ", filters));
        if (Lib.Italic) parts.Add("italics only");
        if (Lib.Query.Trim().Length > 0) parts.Add($"search “{Lib.Query.Trim()}”");
        return parts.Count == 0 ? null! : string.Join(" · ", parts);
    }

    /// <summary>"12 variants · 220 styles · 1,034 glyphs": nell'ordine variante, stile, glifi (quelli del file che ne ha di più).</summary>
    private static string FamilyNote(TypeFamily f)
    {
        int variants = f.VariantNames(false).Count(), styles = f.Count(false);
        var parts = new List<string>
        {
            $"{Fmt.N(variants)} {Fmt.Plural(variants, "variant", "variants")}",
            $"{Fmt.N(styles)} {Fmt.Plural(styles, "style", "styles")}",
        };
        if (f.Styles(false).Select(s => s.Info?.Glyphs ?? 0).DefaultIfEmpty(0).Max() is > 0 and var glyphs)
            parts.Add($"{Fmt.N(glyphs)} {Fmt.Plural(glyphs, "glyph", "glyphs")}");
        return string.Join(" · ", parts);
    }

    /// <summary>"Back to the catalog", "Back to ABC Marfa"…: dove riporta il pulsante delle impostazioni.</summary>
    private string BackLabel => Lib.ViewBeforeSettings switch
    {
        View.Family when Lib.CurrentFamily is { } f => $"Back to {f}",
        View.Style when Lib.CurrentStyle is { } r => $"Back to {r.Family.Name} {r.Style.Label}",
        _ => "Back to the catalog",
    };
}
