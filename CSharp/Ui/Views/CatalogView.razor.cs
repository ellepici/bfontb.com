using Bfontb.Models;
using Bfontb.Services;
using Bfontb.Ui.Parts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bfontb.Ui.Views;

/// <summary>
/// Il catalogo, in due viste: elenco (una riga per famiglia con il testo di prova) o griglia
/// (una scheda «Aa» per famiglia, come in Libro Font, più veloce da scorrere). Entrambe disegnano solo ciò che si vede.
/// Mentre si cerca, ogni famiglia trovata si apre nelle sue varianti (Bodoni, Bodoni Poster, Bodoni Book…):
/// una riga per variante, così si vedono tutte senza entrare nella famiglia.
/// </summary>
public partial class CatalogView : IAsyncDisposable
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    /// <param name="Title">Il nome da mostrare (la variante, mentre si cerca); null = il nome della famiglia.</param>
    /// <param name="Anchor">La sezione della variante nella pagina della famiglia, dove porta il clic sul nome.</param>
    private sealed record Row(TypeFamily Family, FontStyle Style, string Summary, string CardNote, string? Title = null, string? Anchor = null)
    {
        /// <summary>La variante dello stile mostrato: la tendina dello spessore offre i suoi stili.</summary>
        public string Variant { get; init; } = "";
        public IReadOnlyList<DropdownOption> Options { get; init; } = [];
        public string Key => Family.Name + "\u0001" + Variant;
    }

    private List<Row> Rows = [];

    /// <summary>Interlinea del testo di prova nel catalogo (un po' d'aria per ascendenti e discendenti).</summary>
    private const string LineHeight = "1.25";

    /// <summary>
    /// Altezza esatta di ogni riga dell'elenco: margini e intestazione (63 px) più due righe di testo.
    /// Deve essere uguale per tutte le righe, altrimenti in Safari lo scorrimento virtuale lascia zone vuote.
    /// </summary>
    private int RowHeight => 63 + (int)Math.Ceiling(Lib.Size * 1.25 * 2);

    // ---- griglia: le schede crescono con il cursore del corpo; le righe di schede hanno tutte la stessa altezza ----
    private const int Gap = 16;
    private int CardWidth => 104 + (int)Math.Round(Lib.Size * 1.5);          // 44 pt → 170 px
    private int TileHeight => (int)Math.Round(CardWidth * 0.8);
    private int GridRowHeight => TileHeight + 64;                             // riquadro + nome su due righe + spazio
    private double AaSize => Math.Round(CardWidth * 0.36, 1);
    private int Columns => width <= 0 ? 6 : Math.Max(1, (width + Gap) / (CardWidth + Gap));

    private List<Row[]> GridRows = [];
    private int gridColumns;

    private ElementReference wrapEl;
    private DotNetObjectReference<CatalogView>? selfRef;
    private int width;
    private bool watching;

    protected override void OnParametersSet()
    {
        var filters = Lib.Filters;
        var label = string.Join(" ", FontClassifier.All.Where(a => filters.HasFlag(a.Trait)).Select(a => a.Label));
        var q = Lib.Query.Trim();
        if (q.Length > 0) { Rows = Lib.VisibleFamilies().SelectMany(f => VariantRows(f, q, filters, label)).Select(WithPick).ToList(); BuildGrid(); return; }
        Rows = Lib.VisibleFamilies()
            .Select(f => (f, rep: f.Representative(filters)))
            .Where(x => x.rep is not null)
            .Select(x =>
            {
                int n = x.f.Count(Lib.IncludeWeb);
                int v = x.f.VariantNames(false).Count();
                string summary, note = $"{n} {Fmt.Plural(n, "style", "styles")}";
                if (filters != FontTrait.None)
                {
                    int m = x.f.Matching(filters).Count();
                    summary = m > 0 && m < n ? $"{m} {label} {Fmt.Plural(m, "style", "styles")} of {n}" : note;
                    if (m > 0 && m < n) note = $"{m} of {n} styles";
                }
                else summary = note;
                if (v > 1) summary += $" · {v} variants";
                var variant = x.f.Variants.First(kv => kv.Value.Contains(x.rep!)).Key;
                // in corsivo: se la variante del regular non ha corsivi, la riga mostra la prima variante che li ha
                if (Lib.Italic && !x.f.Variants[variant].Any(s => s.Italic))
                    variant = x.f.VariantNames(false).FirstOrDefault(n => x.f.Variants[n].Any(s => s.Italic)) ?? variant;
                return WithPick(new Row(x.f, x.rep!, summary, note) { Variant = variant });
            })
            .ToList();
        BuildGrid();
    }

    /// <summary>
    /// Le righe di una famiglia trovata con la ricerca: una per variante. Se la ricerca trova il nome della famiglia
    /// ci sono tutte le varianti, altrimenti solo quelle che la contengono (es. "condensed"); con i filtri,
    /// solo le varianti che hanno almeno uno stile che risponde.
    /// </summary>
    private IEnumerable<Row> VariantRows(TypeFamily f, string q, FontTrait filters, string label)
    {
        bool wholeFamily = f.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
        var names = f.VariantNames(Lib.IncludeWeb).Where(v => wholeFamily || v.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var v in names)
        {
            var all = f.Variants[v];
            if (Lib.Italic && !all.Any(s => s.Italic)) continue;   // in corsivo: le varianti senza corsivo non ci sono
            var matching = filters == FontTrait.None ? all : all.Where(s => (s.Traits(f.Name, v) & filters) == filters).ToList();
            if (matching.Count == 0) continue;
            var rep = matching.OrderBy(s => s.Italic).ThenBy(s => Math.Abs(s.Weight - 400)).ThenBy(s => s.FileName.Length).First();
            int n = all.Count, m = matching.Count;
            string note = $"{n} {Fmt.Plural(n, "style", "styles")}", summary = note;
            if (m < n) { summary = $"{m} {label} {Fmt.Plural(m, "style", "styles")} of {n}"; note = $"{m} of {n} styles"; }
            // il nome della variante di solito comprende quello della famiglia ("Bodoni Poster"); se no, lo si aggiunge
            string title = v == f.Name || v.StartsWith(f.Name, StringComparison.OrdinalIgnoreCase) ? v : $"{f.Name} {v}";
            yield return new Row(f, rep, summary, note, title, v == f.Name ? null : FontLibrary.VariantAnchor(v)) { Variant = v };
        }
    }

    /// <summary>
    /// La tendina dello spessore della riga (gli stili della sua variante, dal più leggero; con Italic, solo i corsivi) e lo stile mostrato:
    /// quello scelto con la tendina della riga, se c'è; altrimenti il più vicino allo spessore scelto in alto
    /// (Weight, di solito Regular) e dritto o corsivo come chiesto in alto (Italic), tra gli stili che rispondono ai filtri.
    /// </summary>
    private Row WithPick(Row r)
    {
        var styles = r.Family.Variants[r.Variant].OrderBy(s => s.Weight).ThenBy(s => s.Italic).ThenBy(s => s.FileName, StringComparer.Ordinal).ToList();
        // con Italic acceso la tendina (e le maniglie della griglia) offrono solo i corsivi
        if (Lib.Italic && styles.Any(s => s.Italic)) styles = styles.Where(s => s.Italic).ToList();
        var shown = Lib.CatalogPicks.TryGetValue(r.Key, out var path) && styles.FirstOrDefault(s => s.Path == path) is { } picked
            ? picked
            : Nearest(r, styles) ?? r.Style;
        return r with { Style = shown, Options = styles.Select(s => new DropdownOption(s.Path, s.Label)).ToList() };
    }

    private FontStyle? Nearest(Row r, List<FontStyle> styles)
    {
        var filters = Lib.Filters;
        var pool = filters == FontTrait.None ? styles : styles.Where(s => (s.Traits(r.Family.Name, r.Variant) & filters) == filters).ToList();
        if (pool.Count == 0) pool = styles;
        int w = Lib.Weight;
        bool italic = Lib.Italic;   // il corsivo (o il dritto) se la variante ce l'ha, poi lo spessore più vicino
        return pool.OrderBy(s => s.Italic != italic).ThenBy(s => Math.Abs(s.Weight - w)).ThenBy(s => s.Weight).ThenBy(s => s.FileName.Length).FirstOrDefault();
    }

    /// <summary>Scelta dalla tendina di una riga: resta finché non si apre un'altra cartella (anche uscendo dal catalogo).</summary>
    private void PickStyle(Row r, string path)
    {
        Lib.CatalogPicks[r.Key] = path;
        OnParametersSet();
    }

    private void BuildGrid()
    {
        gridColumns = Columns;
        GridRows = Rows.Chunk(gridColumns).ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (watching || disposed) return;
        watching = true;
        selfRef = DotNetObjectReference.Create(this);
        // il catalogo può chiudersi mentre aspetta il JavaScript (l'app si apre su un'altra pagina, es. /app/settings)
        try { await Bridge.WatchWidthAsync(wrapEl, selfRef); }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
    }

    private bool disposed;

    /// <summary>La colonna ha cambiato larghezza (finestra ridimensionata): ricalcoliamo le schede per riga.</summary>
    [JSInvokable]
    public void OnWidth(int w)
    {
        width = w;
        if (Columns == gridColumns) return;
        BuildGrid();
        StateHasChanged();
    }

    private string GridStyle =>
        $"--cols:{gridColumns};--gap:{Gap}px;--tile-h:{TileHeight}px;--grow-h:{GridRowHeight}px";

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        try { await Bridge.UnwatchWidthAsync(wrapEl); } catch (JSException) { }
        selfRef?.Dispose();
    }
}
