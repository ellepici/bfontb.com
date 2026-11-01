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
        /// <summary>Lo stile mostrato è un variabile reso corsivo con il suo asse (slnt/ital).</summary>
        public bool Slanted { get; init; }
        /// <summary>Riga di una sola variante (durante una ricerca): la tendina offre solo i suoi stili.</summary>
        public bool PerVariant { get; init; }
        /// <summary>I variabili tra le scelte di cui non si sa ancora se hanno il corsivo dentro: la riga li fa leggere in sottofondo.</summary>
        public IReadOnlyList<FontStyle> Prefetch { get; init; } = [];
        public string Key => Family.Name + "\u0001" + Variant;
    }

    private List<Row> Rows = [];

    /// <summary>Interlinea del testo di prova nel catalogo (un po' d'aria per ascendenti e discendenti).</summary>
    public const string LineHeight = "1.25";

    /// <summary>
    /// Altezza esatta di ogni riga dell'elenco: margini e intestazione (63 px) più due righe di testo.
    /// Deve essere uguale per tutte le righe, altrimenti in Safari lo scorrimento virtuale lascia zone vuote.
    /// </summary>
    private int RowHeight => RowHeightFor(Lib.Size);

    /// <summary>La stessa misura per le righe della pagina di una famiglia, che si comportano come quelle del catalogo.</summary>
    public static int RowHeightFor(int size) => 63 + (int)Math.Ceiling(size * 1.25 * 2);

    /// <summary>Le variabili CSS dell'elenco a righe (altezza della riga, interlinea), per il catalogo e per la pagina di una famiglia.</summary>
    public static string ListStyle(int size) => $"--row-h:{RowHeightFor(size)}px;--spec-lh:{LineHeight}";

    // ---- griglia: sei schede per riga (meno solo se la colonna è stretta), larghe quanto la colonna permette;
    //      il corpo scelto in alto decide quanto è grande «Aa» dentro la scheda. Le righe di schede hanno tutte la stessa altezza ----
    private const int Gap = 16, MaxColumns = 6, MinCard = 180;
    private int Columns => width <= 0 ? MaxColumns : Math.Clamp((width + Gap) / (MinCard + Gap), 1, MaxColumns);
    private int CardWidth => width <= 0 ? 300 : (width - (Columns - 1) * Gap) / Columns;
    private int TileHeight => (int)Math.Round(CardWidth * 0.8);
    private int GridRowHeight => TileHeight + 64;                             // riquadro + nome su due righe + spazio
    // «Aa» in proporzione alla scheda: un quarto della larghezza a corpi piccoli, fino a quasi metà ai corpi grandi
    private double AaSize => Math.Round(CardWidth * Math.Clamp(0.18 + Lib.Size / 400.0, 0.24, 0.45), 1);

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
                int n = x.f.Count(false);
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
                if (Lib.Italic && !x.f.Variants[variant].Any(s => s.HasItalic))
                    variant = x.f.VariantNames(false).FirstOrDefault(n => x.f.Variants[n].Any(s => s.HasItalic)) ?? variant;
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
        var names = f.VariantNames(false).Where(v => wholeFamily || v.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var v in names)
        {
            var all = f.Variants[v];
            if (Lib.Italic && !all.Any(s => s.HasItalic)) continue;   // in corsivo: le varianti senza corsivo non ci sono
            var matching = filters == FontTrait.None ? all : all.Where(s => (s.Traits(f.Name, v) & filters) == filters).ToList();
            if (matching.Count == 0) continue;
            var rep = matching.OrderBy(s => s.Italic).ThenBy(s => Math.Abs(s.Weight - 400)).ThenBy(s => s.FileName.Length).First();
            int n = all.Count, m = matching.Count;
            string note = $"{n} {Fmt.Plural(n, "style", "styles")}", summary = note;
            if (m < n) { summary = $"{m} {label} {Fmt.Plural(m, "style", "styles")} of {n}"; note = $"{m} of {n} styles"; }
            // il nome della variante di solito comprende quello della famiglia ("Bodoni Poster"); se no, lo si aggiunge
            string title = v == f.Name || v.StartsWith(f.Name, StringComparison.OrdinalIgnoreCase) ? v : $"{f.Name} {v}";
            yield return new Row(f, rep, summary, note, title, v == f.Name ? null : FontLibrary.VariantAnchor(v)) { Variant = v, PerVariant = true };
        }
    }

    /// <summary>
    /// La tendina dello spessore della riga (gli stili della sua variante, dal più leggero; con Italic, solo i corsivi) e lo stile mostrato:
    /// quello scelto con la tendina della riga, se c'è; altrimenti il più vicino allo spessore scelto in alto
    /// (Weight, di solito Regular) e dritto o corsivo come chiesto in alto (Italic), tra gli stili che rispondono ai filtri.
    /// </summary>
    private Row WithPick(Row r)
    {
        static List<FontStyle> Order(IEnumerable<FontStyle> l) => l.OrderBy(s => s.Weight).ThenBy(s => s.Italic).ThenBy(s => s.FileName, StringComparer.Ordinal).ToList();
        // con un filtro acceso la tendina della famiglia raccoglie gli stili che rispondono da tutte le sue varianti
        // (es. con Variable: Flare Variable, Mix Variable, Sans Variable…), prima quelli della variante della riga;
        // senza filtri, o nella riga di una sola variante (ricerca), solo gli stili della variante
        var filters = Lib.Filters;
        bool across = filters != FontTrait.None && !r.PerVariant;
        var variants = across
            ? r.Family.VariantNames(false).OrderBy(v => v == r.Variant ? 0 : 1).ToList()
            : [r.Variant];
        var groups = variants
            .Select(v => (Variant: v, Styles: Order(r.Family.Variants[v].Where(s => !across || (s.Traits(r.Family.Name, v) & filters) == filters))))
            .Where(g => g.Styles.Count > 0)
            .ToList();
        if (groups.Count == 0) groups = [(r.Variant, Order(r.Family.Variants[r.Variant]))];
        // con Italic acceso la tendina (e le maniglie della griglia) offrono solo i corsivi, compresi i variabili che lo hanno dentro
        if (Lib.Italic && groups.Any(g => g.Styles.Any(s => s.HasItalic)))
            groups = groups.Select(g => (g.Variant, g.Styles.Where(s => s.HasItalic).ToList())).Where(g => g.Item2.Count > 0).ToList();

        // le scelte: ogni stile col suo nome (davanti il nome della variante, se non è quella della riga);
        // un variabile con il corsivo dentro offre anche "… Italic" (solo quello, con Italic acceso)
        var options = new List<(FontStyle Style, bool Slant, DropdownOption Option)>();
        foreach (var (variant, styles) in groups)
        {
            var names = StyleLabels.Distinct(styles);
            foreach (var s in styles)
            {
                var name = WithVariant(r.Family.Name, across ? variant : null, names[s].Text);
                var format = StyleLabels.Format(s);   // il formato in piccolo accanto a ogni stile: OTF, TTF, WOFF2…
                if (!s.SlantsToItalic) { options.Add((s, false, new DropdownOption(s.Path, name, format))); continue; }
                if (!Lib.Italic) options.Add((s, false, new DropdownOption(s.Path, name, format)));
                options.Add((s, true, new DropdownOption(s.Path + FontStyle.ItalicMark, name + " Italic", format)));
            }
        }
        var own = groups.FirstOrDefault(g => g.Variant == r.Variant).Styles ?? groups[0].Styles;
        var shown = Lib.CatalogPicks.TryGetValue(r.Key, out var value) && options.FirstOrDefault(o => o.Option.Value == value) is { Style: not null } picked
            ? (picked.Style, picked.Slant)
            : Nearest(r, own) is { } near ? (near, Lib.Italic && near.SlantsToItalic) : (r.Style, false);
        var prefetch = groups.SelectMany(g => g.Styles)
            .Where(s => s.Status != LoadStatus.Ready && (s.Info is null || (s.Info.IsVariable && FontInfo.NeedsAxes(s.Info)))
                        && (s.Info?.IsVariable ?? s.FileName.Contains("Variable", StringComparison.OrdinalIgnoreCase) || s.FileName.Contains("VF")))
            .ToList();
        return r with { Style = shown.Item1, Slanted = shown.Item2, Options = options.Select(o => o.Option).ToList(), Prefetch = prefetch };
    }

    /// <summary>
    /// Il nome di uno stile di un'altra variante: la parte che distingue la variante ("ABC Arizona Flare Variable" → "Flare Variable")
    /// seguita dalle parole dello stile che non dice già ("Variable" → niente, "Bold" → "Flare Bold").
    /// </summary>
    private static string WithVariant(string family, string? variant, string label)
    {
        if (variant is null) return label;
        var part = variant.StartsWith(family, StringComparison.OrdinalIgnoreCase) ? variant[family.Length..].Trim() : variant;
        if (part.Length == 0) return label;
        var have = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var rest = label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !have.Contains(w, StringComparer.OrdinalIgnoreCase));
        return string.Join(" ", have.Concat(rest));
    }

    private FontStyle? Nearest(Row r, List<FontStyle> styles)
    {
        var filters = Lib.Filters;
        var pool = filters == FontTrait.None ? styles : styles.Where(s => (s.Traits(r.Family.Name, VariantOf(r.Family, s)) & filters) == filters).ToList();
        if (pool.Count == 0) pool = styles;
        int w = Lib.Weight;
        bool italic = Lib.Italic;   // il corsivo (o il dritto) se la variante ce l'ha, poi lo spessore più vicino
        return pool.OrderBy(s => (italic ? s.HasItalic : s.Italic) != italic).ThenBy(s => Math.Abs(s.Weight - w)).ThenBy(s => s.Weight).ThenBy(s => s.FileName.Length).FirstOrDefault();
    }

    private static string VariantOf(TypeFamily f, FontStyle s) => f.Variants.FirstOrDefault(kv => kv.Value.Contains(s)).Key ?? f.Name;

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

    /// <summary>La colonna ha cambiato larghezza (finestra ridimensionata): ricalcoliamo le schede per riga e la loro misura.</summary>
    [JSInvokable]
    public void OnWidth(int w)
    {
        if (w == width) return;
        width = w;
        if (Columns != gridColumns) BuildGrid();
        StateHasChanged();   // anche con le stesse colonne: le schede si allargano o si stringono con la colonna
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
