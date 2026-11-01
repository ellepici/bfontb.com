using System.Globalization;
using Bfontb.Models;
using Bfontb.Services;
using Bfontb.Ui.Parts;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

/// <summary>La pagina di uno stile: tutti i caratteri del file, la prova a vari corpi e, a richiesta, i crediti.</summary>
public partial class StyleView
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private FontLoader Loader { get; set; } = default!;
    [Inject] private GlyphLoader Glyphs { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    private enum Tab { Glyphs, Proof }

    private static int[] Sizes => FontLibrary.Sizes;

    private const string Paragraph =
        "Typography is the craft of arranging type so that a text reads well and says something through its form as well. " +
        "A good text face goes almost unnoticed: it keeps the rhythm of the line, clearly tells apart similar letters such as l, I and 1, " +
        "and holds up both at the small sizes of a caption and at the large sizes of a headline. Try the same sentence at different sizes " +
        "and watch how spacing and contrast change.";

    private Tab tab = Tab.Glyphs;
    private bool showCredits;

    private void ToggleCredits() => showCredits = !showCredits;

    // Esc chiude i crediti, come le altre tendine
    private void OnCredKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e) { if (e.Key == "Escape") showCredits = false; }
    private FontLibrary.StyleRef? Ref;
    private FontStyle? loadedFor;
    private GlyphSet? Set;
    private List<(string Name, int Count)> VariantList = [];
    private List<FontStyle> VariantStyles = [];
    private List<DropdownOption> VariantOptions = [];
    private List<DropdownOption> StyleOptions = [];

    protected override async Task OnParametersSetAsync()
    {
        Ref = Lib.CurrentStyle;
        if (Ref is null) return;
        Neighbours(Ref);
        var st = Ref.Style;
        if (ReferenceEquals(loadedFor, st) && (Set is not null || st.IsDemo || !Lib.Connected)) return;
        loadedFor = st;
        Set = null;
        if (st.IsDemo || !Lib.Connected) return;

        var face = Loader.EnsureAsync(st, FontLoader.Asked, full: true);
        var set = Glyphs.GetAsync(st);
        await face;
        if (!ReferenceEquals(loadedFor, st)) return;
        StateHasChanged();
        var result = await set;
        if (ReferenceEquals(loadedFor, st)) Set = result;
    }

    private static IEnumerable<FontStyle> Sorted(IEnumerable<FontStyle> list) =>
        list.OrderBy(s => s.Weight).ThenBy(s => s.Italic).ThenBy(s => s.FileName, StringComparer.Ordinal);

    /// <summary>Varianti della famiglia e stili della variante, nello stesso ordine della pagina della famiglia.</summary>
    private void Neighbours(FontLibrary.StyleRef r)
    {
        var names = r.Family.VariantNames(r.Variant == "Web").ToList();
        VariantList = names.Select(v => (v, r.Family.Variants[v].Count)).ToList();
        VariantStyles = Sorted(r.Family.Variants[r.Variant]).ToList();
        VariantOptions = VariantList.Select(v => new DropdownOption(v.Name, v.Name, $"{v.Count} {Fmt.Plural(v.Count, "style", "styles")}")).ToList();
        // gli stessi nomi della tendina del catalogo, mai due uguali (vedi StyleLabels)
        var labels = StyleLabels.Distinct(VariantStyles);
        StyleOptions = VariantStyles.Select(s => new DropdownOption(s.Path, labels[s].Text, labels[s].Format)).ToList();
    }

    private void Open(FontStyle? s)
    {
        if (s is not null && Ref is not null) Lib.OpenStyle(s, Ref.Family.Name);
    }

    /// <summary>Un'altra variante: si apre lo stile con lo stesso nome (Book, Bold…), o il più simile.</summary>
    private void OnVariant(string v)
    {
        if (Ref is not null) Lib.OpenVariant(Ref.Family, v, Ref.Style);
    }

    private void OnStyle(string path) => Open(VariantStyles.FirstOrDefault(s => s.Path == path));

    private void Show(Tab t)
    {
        tab = t;
        Lib.SetStyleTab(t == Tab.Glyphs);   // i comandi in alto si accendono o si spengono di conseguenza
    }

    // la pagina si apre sempre sui caratteri: lo sa anche la barra dei comandi
    protected override void OnInitialized() => Lib.SetStyleTab(true);
    private string TabSel(Tab t) => tab == t ? "true" : "false";

    private bool Drawable(FontStyle s) => s.IsDemo || s.Status == LoadStatus.Ready;


    private static string FileLine(FontStyle s) => s.FileName + (s.Size > 0 ? " · " + Fmt.Kb(s.Size) : "");

    // l'ultimo stile di cui si sono letti i caratteri: si continua a mostrarlo mentre si legge il successivo
    private GlyphSet? lastSet;
    private FontStyle? lastStyle;

    private bool Ready => Set is { Error: null } && Ref is not null && Drawable(Ref.Style);
    private GlyphSet? ShownSet => Ready ? Set : lastSet;
    private FontStyle? ShownStyle => Ready ? Ref!.Style : lastStyle;

    /// <summary>Il numero sulla scheda "Characters" (animato): quello dello stile mostrato, anche mentre si legge il successivo.</summary>
    private long? TabCount => ShownSet is { } s ? s.Encoded + s.Alternates : null;

    protected override void OnAfterRender(bool firstRender)
    {
        if (Ready) { lastSet = Set; lastStyle = Ref!.Style; }
    }

    private string StyleAt(FontStyle s, int size) =>
        $"{s.Css};font-size:{size}px;letter-spacing:{(Lib.Tracking / 1000.0).ToString(CultureInfo.InvariantCulture)}em";

    // "ABC Asfalt Variable Variable" no: se la variante dice già Variable, lo stile mostra solo il resto (Italic o niente)
    private string ShownLabel(FontStyle s) =>
        Ref is { } r && r.Variant.Contains("Variable", StringComparison.OrdinalIgnoreCase) && s.Label.StartsWith("Variable", StringComparison.Ordinal)
            ? s.Label["Variable".Length..].Trim()
            : s.Label;
}
