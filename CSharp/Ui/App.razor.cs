using Bfontb.Models;
using Bfontb.Services;
using Bfontb.Ui.Parts;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bfontb.Ui;

public partial class App : IDisposable
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private BrowserBridge Bridge { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private ElementReference pickInput;
    private DotNetObjectReference<FontLibrary>? libRef;
    private CancellationTokenSource? queryDelay;

    /// <summary>Righe dell'indice a sinistra: la lettera compare solo sulla prima famiglia che la usa.</summary>
    private sealed record IndexRow(string? Letter, TypeFamily Family);
    private List<IndexRow> IndexRows = [];

    /// <summary>Un filtro per caratteristica, con il numero di famiglie che la hanno tra quelle cercate.</summary>
    private sealed record TraitChip(FontTrait Trait, string Label, int Count, bool Active);
    private List<TraitChip> TraitChips = [];

    /// <summary>Le tre scelte dell'aspetto, con le icone Phosphor.</summary>
    private string SampleBoxValue => Lib.SampleText == FontLibrary.NameToken ? "" : Lib.SampleText;

    protected override async Task OnInitializedAsync()
    {
        Lib.Changed += OnLibraryChanged;
        // l'indirizzo con cui si è aperta l'app (/app/family/…): solo da leggere, le viste le cambia l'app
        await Lib.InitAsync("/" + Nav.ToBaseRelativePath(Nav.Uri));
        BuildIndex();
    }

    /// <summary>
    /// Cosa mostra la colonna dei caratteri: quando cambia, la si riporta in cima; tornando al catalogo (con gli stessi
    /// filtri, ricerca e griglia/elenco) si riparte invece da dove lo si era lasciato. Vedi showView in bfontb.js.
    /// </summary>
    private string? shownKey;
    private string ShownKey => Lib.View switch
    {
        View.Family => $"{Lib.View}|{Lib.CurrentFamily}|{Lib.Filters}",
        View.Style => $"{Lib.View}|{Lib.CurrentStylePath}",
        _ => $"{Lib.View}|{Lib.Query}|{Lib.Filters}|{Lib.GridView}",
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var key = ShownKey;
        if (firstRender)
        {
            shownKey = key;
            libRef = DotNetObjectReference.Create(Lib);
            await Bridge.InitAsync(libRef, pickInput);
            await SyncUrlAsync();
            return;
        }
        if (key != shownKey)
        {
            shownKey = key;
            await Bridge.ShowViewAsync(key, restore: Lib.View == View.Catalog);
        }
        await RevealFamilyAsync();
        await SyncUrlAsync();
    }

    /// <summary>
    /// Aggiorna l'indirizzo e il titolo della scheda quando cambia la vista: un passo nuovo nella cronologia, così
    /// Indietro e Avanti del browser funzionano (filtri, ricerca e dimensioni non cambiano l'indirizzo).
    /// </summary>
    /// <summary>
    /// Aperta una famiglia da fuori dell'indice (catalogo, percorso, Indietro…): l'indice a sinistra scorre finché
    /// la famiglia è in cima, così si vedono anche le sue varianti. Se la si è scelta nell'indice, l'indice resta fermo.
    /// </summary>
    private string? revealedFamily;
    private bool pickedInIndex;
    private async Task RevealFamilyAsync()
    {
        var current = Lib.View is View.Family or View.Style && !Lib.IsDemo ? Lib.CurrentFamily : null;
        if (current == revealedFamily) return;
        revealedFamily = current;
        bool fromIndex = pickedInIndex;
        pickedInIndex = false;
        if (current is not null && !fromIndex) await Bridge.RevealFamilyAsync();
    }

    private string? shownTitle;
    private async Task SyncUrlAsync()
    {
        var url = Lib.UrlFor();
        var title = Lib.TitleFor();
        if (url == Lib.ShownUrl && !Lib.ReplaceUrl)
        {
            if (title != shownTitle) { shownTitle = title; await Bridge.SetTitleAsync(title); }
            return;
        }
        var replace = Lib.ReplaceUrl;
        Lib.ReplaceUrl = false;
        Lib.ShownUrl = url;
        shownTitle = title;
        await Bridge.GoUrlAsync(url, title, replace);
    }

    private void OnLibraryChanged()
    {
        BuildIndex();
        InvokeAsync(StateHasChanged);
    }

    private void BuildIndex()
    {
        var rows = new List<IndexRow>();
        string last = "";
        foreach (var f in Lib.VisibleFamilies())
        {
            var letter = LetterOf(f.Name);
            rows.Add(new IndexRow(letter != last ? letter : null, f));
            last = letter;
        }
        IndexRows = rows;

        // conteggi calcolati sulle famiglie che rispondono anche agli altri filtri attivi
        var searched = Lib.SearchedFamilies().Select(f => f.Traits()).ToList();
        TraitChips = FontClassifier.All.Where(a => a.Trait != FontTrait.Symbol).Select(a =>   // "Symbols" non è fra i filtri
        {
            var need = Lib.Filters | a.Trait;
            int count = searched.Count(t => (t & need) == need);
            return new TraitChip(a.Trait, a.Label, count, Lib.Filters.HasFlag(a.Trait));
        }).ToList();
    }

    private static string LetterOf(string name)
    {
        var c = char.ToUpperInvariant(name.FirstOrDefault(' '));
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }

    /// <summary>Link social in alto a destra, accanto a Settings (icone Phosphor). Indirizzi da sostituire con i profili veri.</summary>
    private static readonly (string Name, string Icon, string Url)[] Social =
    [
        ("Instagram", "instagram-logo", "https://www.instagram.com/"),
        ("Threads", "threads-logo", "https://www.threads.com/"),
        ("X", "x-logo", "https://x.com/"),
    ];
    /// <summary>Le varianti da mostrare sotto la famiglia aperta (nessuna se ne ha una sola con il suo stesso nome).</summary>
    private List<(string Name, int Count)> OpenVariants(TypeFamily f)
    {
        if (Lib.View is not (View.Family or View.Style) || Lib.CurrentFamily != f.Name) return [];
        var names = f.VariantNames(Lib.IncludeWeb).ToList();
        if (names.Count == 0 || (names.Count == 1 && names[0] == f.Name)) return [];
        return names.Select(v => (v, f.Variants[v].Count)).ToList();
    }

    private string IsCurrentVariant(TypeFamily f, string variant) =>
        Lib.View == View.Style && Lib.CurrentStyle is { } r && r.Family == f && r.Variant == variant ? "true" : "false";

    /// <summary>
    /// Nella pagina di uno stile si passa alla variante tenendo lo stesso stile (Book, Bold…);
    /// dalla pagina della famiglia si apre la pagina dei glifi della variante, partendo dal suo stile più sottile.
    /// </summary>
    private void OpenVariant(TypeFamily f, string variant)
    {
        pickedInIndex = true;
        if (Lib.View == View.Style) { Lib.OpenVariant(f, variant, Lib.CurrentStyle?.Style); return; }
        if (!f.Variants.TryGetValue(variant, out var list) || list.Count == 0) return;
        var lightest = list.OrderBy(s => s.Weight).ThenBy(s => s.Italic).ThenBy(s => s.FileName, StringComparer.Ordinal).First();
        Lib.OpenStyle(lightest, f.Name);
    }

    /// <summary>
    /// Clic su una famiglia della colonna di sinistra: la apre; se è già aperta (nella sua pagina), un secondo clic
    /// la richiude e si torna al catalogo, come togliendo la selezione.
    /// </summary>
    private void PickFamily(TypeFamily f)
    {
        pickedInIndex = true;
        if (Lib.View == View.Family && Lib.CurrentFamily == f.Name) Lib.ShowCatalog();
        else Lib.OpenFamily(f.Name);
    }

    private string IsCurrent(TypeFamily f) => Lib.View is View.Family or View.Style && Lib.CurrentFamily == f.Name ? "true" : "false";
    // ---- menu in alto a destra: impostazioni e social ----
    private bool menuOpen;

    private void ToggleMenu() => menuOpen = !menuOpen;
    private void CloseMenu() => menuOpen = false;

    /// <summary>"Change folder" è un'etichetta del campo file: il menu si chiude subito dopo, quando il browser ha già aperto la scelta.</summary>
    private async Task CloseMenuSoon()
    {
        await Task.Delay(50);
        menuOpen = false;
    }

    // ---- crediti, segnalazioni e suggerimenti (dal menu): pagine come le impostazioni ----
    private void MenuPage(View page) { menuOpen = false; Lib.OpenPage(page); }
    private void MenuFeedback(FeedbackKind kind) { menuOpen = false; Lib.OpenFeedback(kind); }
    private bool IsFeedback(FeedbackKind kind) => Lib.View == View.Feedback && Lib.Feedback == kind;

    private static readonly (string Value, string Icon, string Label)[] Themes =
    [
        ("light", "sun", "Light"),
        ("dark", "moon", "Dark"),
    ];

    private void OnMenuKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key == "Escape") menuOpen = false;
    }

    /// <summary>Impostazioni dal menu: le apre e, se sono già aperte, le chiude (si torna dove si era).</summary>
    private void MenuSettings()
    {
        menuOpen = false;
        if (Lib.View == View.Settings) Lib.CloseSettings();
        else Lib.OpenSettings();
    }

    private Task OnSampleInput(ChangeEventArgs e) => Lib.SetSampleAsync(e.Value?.ToString() ?? "");
    private Task UsePreset(string text) => Lib.SetSampleAsync(text);

    // testi pronti in una tendina: il nome e, in grigio, l'inizio del testo
    private static readonly DropdownOption[] PresetOptions = FontLibrary.Presets
        .Select(p => new DropdownOption(p.Key, p.Key, PresetPreview(p.Value))).ToArray();

    private static string PresetPreview(string text) =>
        text == FontLibrary.NameToken ? "the family’s name" : text.Length > 24 ? text[..22].TrimEnd() + "…" : text;

    /// <summary>Il testo pronto in uso; "Custom" se il testo è stato scritto a mano (nessuna scelta spuntata).</summary>
    private string CurrentPreset => FontLibrary.Presets.FirstOrDefault(p => p.Value == Lib.SampleText).Key ?? "Custom";

    private Task UsePresetNamed(string name) =>
        FontLibrary.Presets.TryGetValue(name, out var text) ? Lib.SetSampleAsync(text) : Task.CompletedTask;
    private static readonly DropdownOption[] SizeOptions = FontLibrary.Sizes.Select(s => new DropdownOption(s.ToString(), $"{s} pt")).ToArray();
    private static readonly DropdownOption[] TrackOptions = FontLibrary.TrackingSteps.Select(t => new DropdownOption(t.ToString(), TrackLabel(t))).ToArray();

    // valori scritti a mano: "48", "48 pt", "10,5" (arrotondato); tracking "−50", "-50", "+25"
    private const int MinSize = 4, MaxSize = 400, MinTracking = -500, MaxTracking = 1000;

    private static string? ParseSize(string s) =>
        ParseNumber(s.Replace("pt", "", StringComparison.OrdinalIgnoreCase)) is double d ? Math.Clamp((int)Math.Round(d), MinSize, MaxSize).ToString() : null;

    private static string? ParseTracking(string s) =>
        ParseNumber(s) is double d ? Math.Clamp((int)Math.Round(d), MinTracking, MaxTracking).ToString() : null;

    private static string FormatTracking(string v) => int.TryParse(v, out var t) ? TrackLabel(t) : v;

    private static double? ParseNumber(string s)
    {
        s = s.Trim().Replace('\u2212', '-').Replace(',', '.').Replace(" ", "").TrimStart('+');
        return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;
    }

    /// <summary>I colori scelti per i testi di prova, per il CSS (vedi .app.tinted in specimens.css e style-page.css).</summary>
    private string? ColorVars =>
        (Lib.InkColor is { } ink ? $"--spec-ink:{ink};" : "") + (Lib.PaperColor is { } paper ? $"--spec-paper:{paper};" : "") is { Length: > 0 } v ? v : null;

    private Task OnSize(string v) => int.TryParse(v, out var s) ? Lib.SetSizeAsync(s) : Task.CompletedTask;

    private static readonly DropdownOption[] WeightOptions = FontLibrary.Weights.Select(w => new DropdownOption(w.Value.ToString(), w.Name, w.Value.ToString())).ToArray();
    private Task OnWeight(string v) => int.TryParse(v, out var w) ? Lib.SetWeightAsync(w) : Task.CompletedTask;

    /// <summary>−50, 0, +25: il segno dice subito se le lettere si stringono o si allargano.</summary>
    private static string TrackLabel(int t) => t switch { < 0 => $"\u2212{-t}", > 0 => $"+{t}", _ => "0" };

    private void OnTrack(string v)
    {
        if (int.TryParse(v, out var t)) _ = Lib.SetTrackingAsync(t);
    }

    private void OnWebToggle(ChangeEventArgs e)
    {
        _ = Lib.SetIncludeWebAsync(e.Value is true);
    }

    private async Task OnQueryInput(ChangeEventArgs e)
    {
        queryDelay?.Cancel();
        queryDelay = new CancellationTokenSource();
        var token = queryDelay.Token;
        try { await Task.Delay(150, token); } catch (TaskCanceledException) { return; }
        await Lib.SetQueryAsync(e.Value?.ToString() ?? "");
    }

    public void Dispose()
    {
        Lib.Changed -= OnLibraryChanged;
        libRef?.Dispose();
    }
}
