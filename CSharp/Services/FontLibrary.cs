using System.Text.Json;
using Bfontb.Models;
using Microsoft.JSInterop;

namespace Bfontb.Services;

/// <summary>Stato dell'app: cartella aperta, famiglie, impostazioni di prova, scelte dell'utente.</summary>
public sealed class FontLibrary(BrowserBridge bridge, FontLoader loader, AnalysisCache cache, GlyphLoader glyphs)
{
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        ["Pangram"] = "The quick brown fox jumps over the lazy dog.",
        ["Name"] = NameToken,
        ["Alphabet"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz",
        ["Figures"] = "0123456789 € $ £ % & @ ¶ § (1/2) «àèéìòù»",
    };

    /// <summary>Testi pronti salvati nel browser che non ci sono più (versione italiana, Title, Letters): diventano quelli attuali.</summary>
    private static readonly Dictionary<string, string> OldPresets = new()
    {
        ["Quel vituperabile xenofobo zelante assaggia il whisky ed esclama: alleluja!"] = "The quick brown fox jumps over the lazy dog.",
        ["Milano, Scuola del Design"] = "The quick brown fox jumps over the lazy dog.",
        // testi pronti tolti (Title e Letters): chi li aveva scelti torna al pangramma
        ["Milan, School of Design"] = "The quick brown fox jumps over the lazy dog.",
        ["Rag Hamburgefonstiv"] = "The quick brown fox jumps over the lazy dog.",
    };
    public const string NameToken = "__NAME__";

    public event Action? Changed;

    /// <summary>Cresce a ogni cambiamento: le viste lo ricevono come parametro e così si ridisegnano.</summary>
    public int Version { get; private set; }

    private void OnSlantFound() { infoStamp++; Notify(); }

    public void Notify()
    {
        Version++;
        Changed?.Invoke();
    }

    // ---- cartella ----
    public List<FontFile> Files { get; private set; } = [];
    public string RootName { get; private set; } = "";
    public bool IsDemo { get; private set; } = true;

    /// <summary>
    /// False dopo un ricaricamento: famiglie e crediti vengono dalla memoria del browser,
    /// ma i file vanno ricollegati (il browser non conserva il permesso di leggere la cartella).
    /// </summary>
    public bool Connected { get; private set; }
    public string? StatusMessage { get; set; }
    /// <summary>
    /// Le famiglie che l'app mostra: quelle della cartella senza i font web, oppure (filtro Web acceso) solo i font web,
    /// ognuno sotto la sua famiglia come variante principale. AllFamilies: tutte, così come le ha costruite l'organizzazione.
    /// </summary>
    public Dictionary<string, TypeFamily> Families
    {
        get => WebOnly ? (_web ??= WebView(_all)) : _all;
        private set { _all = value; _web = null; }
    }
    public Dictionary<string, TypeFamily> AllFamilies => _all;
    private Dictionary<string, TypeFamily> _all = DemoFonts.Create();
    private Dictionary<string, TypeFamily>? _web;

    /// <summary>Filtro Web: si vedono solo i font per il web (woff, woff2). Si ricorda.</summary>
    public bool WebOnly { get; private set; }

    public async Task SetWebOnlyAsync(bool on)
    {
        WebOnly = on;
        CatalogPicks.Clear();
        await SetAsync("pf.webonly", WebOnly);
        Notify();
    }

    // le famiglie con i soli font web: la loro variante "Web" diventa quella principale (col nome della famiglia)
    private static Dictionary<string, TypeFamily> WebView(Dictionary<string, TypeFamily> all)
    {
        var web = new Dictionary<string, TypeFamily>(all.Comparer);
        foreach (var f in all.Values)
        {
            if (!f.Variants.TryGetValue("Web", out var list) || list.Count == 0) continue;
            var w = new TypeFamily(f.Name);
            foreach (var s in list) w.Add(f.Name, s);
            web[f.Name] = w;
        }
        return web;
    }

    /// <summary>Quante famiglie hanno font web e rispondono a ricerca e filtri (il numero accanto a "Web").</summary>
    public int WebFamilyCount() =>
        SearchedFamilies(_web ??= WebView(_all)).Count(f => (Filters == FontTrait.None || (f.Traits() & Filters) == Filters) && MatchesItalic(f));

    /// <summary>Con Italic acceso contano solo le famiglie che hanno corsivi (come nell'elenco a sinistra e nel catalogo).</summary>
    public bool MatchesItalic(TypeFamily f) => !Italic || f.Styles(false).Any(s => s.HasItalic);

    // ---- analisi e correzioni ----
    public Dictionary<string, FontInfo> Analysis { get; private set; } = new(StringComparer.Ordinal);
    public Proposal? Proposal { get; private set; }
    public Corrections Fix { get; private set; } = new();
    public OrgMode Mode { get; private set; } = OrgMode.Folders;
    public bool AnalyzeWeb { get; private set; }

    // ---- prova ----
    public View View { get; set; } = View.Catalog;
    public string? CurrentFamily { get; set; }
    public string Query { get; set; } = "";
    public string PropQuery { get; set; } = "";
    public string SampleText { get; private set; } = Presets["Pangram"];
    /// <summary>Corpi tra cui scegliere (tendina in alto e cascata della pagina dello stile).</summary>
    public static readonly int[] Sizes = [10, 12, 14, 18, 24, 32, 48, 64, 96, 128];

    /// <summary>Spaziature tra cui scegliere, in millesimi di em.</summary>
    public static readonly int[] TrackingSteps = [-100, -50, -25, -10, 0, 10, 25, 50, 100, 200];

    public int Size { get; private set; } = 48;

    /// <summary>
    /// Spessore del catalogo: ogni riga (e scheda) mostra lo stile più vicino a questo peso, dritto se c'è.
    /// Le righe in cui si è scelto uno stile con la loro tendina restano come le si è lasciate (vedi CatalogPicks),
    /// finché non si cambia di nuovo lo spessore o il corsivo generale: allora tornano a seguirlo anche loro.
    /// Non si ricorda: a ogni apertura della cartella (anche dopo un ricaricamento) torna a Regular.
    /// </summary>
    public int Weight { get; private set; } = 400;

    /// <summary>
    /// Catalogo in corsivo: ogni riga mostra il corsivo più vicino allo spessore, e le famiglie (e, cercando, le varianti)
    /// senza corsivo non si vedono, come con un filtro.
    /// </summary>
    public bool Italic { get; private set; }

    /// <summary>
    /// Colori dei testi di prova (catalogo, famiglia, caratteri, prova): testo e sfondo in esadecimale ("#1A1A1A").
    /// Null = quelli dell'interfaccia, che seguono il tema chiaro o scuro. Come lo spessore, non si ricordano.
    /// </summary>
    public string? InkColor { get; private set; }
    public string? PaperColor { get; private set; }

    public Task SetColorsAsync(string? ink, string? paper) { InkColor = ink; PaperColor = paper; Notify(); return Task.CompletedTask; }

    /// <summary>Scambia testo e sfondo; con i colori del tema (non ancora scelti) si parte da inchiostro su bianco.</summary>
    public Task SwapColorsAsync() => SetColorsAsync(PaperColor ?? "#FFFFFF", InkColor ?? "#16171A");

    /// <summary>Come si scrive il codice del colore nelle tendine: "hex" (#E85D9E) o "rgb" (232, 93, 158). Si ricorda.</summary>
    public string ColorFormat { get; private set; } = "hex";

    public async Task SetColorFormatAsync(string format)
    {
        ColorFormat = format == "rgb" ? "rgb" : "hex";
        await SetAsync("pf.cfmt", ColorFormat);
        Notify();
    }

    /// <summary>Gli ultimi colori personalizzati usati (testo e sfondo insieme), il più recente per primo. Si ricordano.</summary>
    public List<string> RecentColors { get; private set; } = [];
    public const int RecentMax = 9;   // una riga della tavolozza

    // l'ultimo colore messo nei recenti e quando: i ritocchi entro pochi secondi lo aggiornano invece di aggiungerne altri
    private DateTime recentAt = DateTime.MinValue;
    private static readonly TimeSpan RecentWindow = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Un colore personalizzato appena scelto (barre o codice) va subito nei recenti. Se il precedente è di meno di 6 secondi fa
    /// (si sta ancora cercando la tinta giusta) prende il suo posto; se no si aggiunge in cima. Niente doppioni, al massimo una riga.
    /// </summary>
    public async Task RememberColorAsync(string hex)
    {
        var now = DateTime.UtcNow;
        bool editing = now - recentAt < RecentWindow && RecentColors.Count > 0;
        recentAt = now;
        if (editing) RecentColors.RemoveAt(0);
        RecentColors.RemoveAll(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
        RecentColors.Insert(0, hex);
        if (RecentColors.Count > RecentMax) RecentColors.RemoveRange(RecentMax, RecentColors.Count - RecentMax);
        await SetAsync("pf.recent", RecentColors);
        Notify();
    }

    /// <summary>Scelto un colore già pronto (tavolozza o recenti): il prossimo colore personalizzato sarà uno nuovo, non un ritocco.</summary>
    public void EndColorEdit() => recentAt = DateTime.MinValue;

    /// <summary>Gli spessori tra cui scegliere, con i nomi usuali.</summary>
    public static readonly (int Value, string Name)[] Weights =
    [
        (100, "Thin"), (200, "Extra Light"), (300, "Light"), (400, "Regular"), (500, "Medium"),
        (600, "Semibold"), (700, "Bold"), (800, "Extra Bold"), (900, "Black"),
    ];

    /// <summary>
    /// Catalogo a griglia (schede «Aa», veloce da scorrere) invece che a elenco (righe con il testo di prova).
    /// L'app si apre sempre a elenco; la griglia vale fino al prossimo ricaricamento.
    /// </summary>
    public bool GridView { get; private set; }

    /// <summary>Aspetto dell'interfaccia: "light", "dark" o "auto" (segue il sistema).</summary>
    public string Theme { get; private set; } = "auto";
    public int Tracking { get; set; }

    /// <summary>Filtri per caratteristica attivi: una famiglia deve averle tutte.</summary>
    public FontTrait Filters { get; private set; }

    /// <summary>Percorso dello stile aperto nella sua pagina (vista Stile).</summary>
    public string? CurrentStylePath { get; private set; }

    public sealed record StyleRef(FontStyle Style, TypeFamily Family, string Variant);

    private (Dictionary<string, TypeFamily>? In, string? Path, StyleRef? Found) _styleCache;

    /// <summary>Lo stile aperto, cercato di nuovo quando le famiglie vengono ricostruite.</summary>
    public StyleRef? CurrentStyle
    {
        get
        {
            if (!ReferenceEquals(_styleCache.In, Families) || _styleCache.Path != CurrentStylePath)
                _styleCache = (Families, CurrentStylePath, FindStyle(CurrentStylePath));
            return _styleCache.Found;
        }
    }

    public StyleRef? FindStyle(string? path)
    {
        if (path is null) return null;
        foreach (var source in new[] { Families, _all })
            foreach (var f in source.Values)
                foreach (var (variant, list) in f.Variants)
                    foreach (var s in list)
                        if (s.Path == path) return new StyleRef(s, f, variant);
        return null;
    }

    /// <summary>
    /// Apre una variante (sottofamiglia) nella pagina dello stile: lo stile con lo stesso nome di quello aperto
    /// (Book, Bold…), altrimenti il più simile per peso e corsivo; senza uno stile aperto, il regular.
    /// </summary>
    public void OpenVariant(TypeFamily family, string variant, FontStyle? like)
    {
        if (!family.Variants.TryGetValue(variant, out var list) || list.Count == 0) return;
        var pick = like is null
            ? list.OrderBy(s => s.Italic).ThenBy(s => Math.Abs(s.Weight - 400)).ThenBy(s => s.FileName.Length).First()
            : list.FirstOrDefault(s => string.Equals(s.Label, like.Label, StringComparison.OrdinalIgnoreCase))
              ?? list.OrderBy(s => s.Italic == like.Italic ? 0 : 1).ThenBy(s => Math.Abs(s.Weight - like.Weight)).ThenBy(s => s.FileName.Length).First();
        OpenStyle(pick, family.Name);
    }

    /// <summary>L'id della sezione di una variante nella pagina della famiglia (per saltarci dall'elenco a sinistra).</summary>
    public static string VariantAnchor(string variant) => "var-" + string.Concat(variant.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>Apre la pagina di uno stile nella colonna dei caratteri.</summary>
    public void OpenStyle(FontStyle st, string family)
    {
        View = View.Style;
        CurrentStylePath = st.Path;
        CurrentFamily = family;
        Navigated();
    }

    public string TextFor(string family) => SampleText == NameToken ? family : SampleText;

    /// <summary>
    /// Il testo di prova è una sequenza di caratteri più che di parole (Alphabet, Figures, o un testo con una "parola"
    /// lunghissima): nel catalogo va a capo anche in mezzo, così la seconda riga si riempie invece di fermarsi a metà
    /// e mandare tutta la parola lunga oltre il limite delle due righe.
    /// </summary>
    public bool SampleIsCharacters =>
        SampleText == Presets["Alphabet"] || SampleText == Presets["Figures"]
        || SampleText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length >= 14);

    /// <param name="path">L'indirizzo con cui si è aperta l'app (/app, /app/family/abc-marfa…): decide la vista.</param>
    public async Task InitAsync(string path)
    {
        loader.SlantFound -= OnSlantFound;
        loader.SlantFound += OnSlantFound;   // un variabile con il corsivo dentro, appena scoperto: tendine e conteggi si rifanno
        SampleText = await GetAsync("pf.text", SampleText);
        if (OldPresets.TryGetValue(SampleText, out var english)) SampleText = english;
        Size = Math.Clamp(await GetAsync("pf.size", Size), 4, 400);
        Theme = await GetAsync("pf.theme", "auto");
        Filters = await GetAsync("pf.filters", FontTrait.None) & ~FontTrait.Symbol;   // filtro tolto: se era attivo, si spegne
        // anche ricerca, spaziatura e font Web restano dopo un ricaricamento (finché non si apre un'altra cartella)
        Query = await GetAsync("pf.query", "");
        Tracking = Math.Clamp(await GetAsync("pf.track", 0), -500, 1000);
        WebOnly = await GetAsync("pf.webonly", false);
        ColorFormat = await GetAsync("pf.cfmt", "hex") == "rgb" ? "rgb" : "hex";
        RecentColors = (await GetAsync("pf.recent", new List<string>())).Take(RecentMax).ToList();
        await RestoreLastFolderAsync();
        // la vista la dice l'indirizzo (prima la si ricordava nel localStorage)
        ShownUrl = path;
        ApplyUrl(path);
        ReplaceUrl = true;
        Notify();
    }

    /// <summary>Ricostruisce l'ultima cartella aperta senza i file: elenco, analisi e correzioni.</summary>
    private async Task RestoreLastFolderAsync()
    {
        var root = await GetAsync<string?>("pf.lastRoot", null);
        if (string.IsNullOrEmpty(root)) return;
        var listing = await cache.LoadListingAsync(root);
        if (listing is null || listing.Count == 0) return;
        Files = listing.Select(l => new FontFile(-1, l.Path, l.Size)).ToList();
        await OpenFilesAsync(root, connected: false);
    }

    public async Task SetSampleAsync(string text) { SampleText = string.IsNullOrEmpty(text) ? " " : text; await SetAsync("pf.text", SampleText); Notify(); }
    public async Task SetSizeAsync(int size) { Size = size; await SetAsync("pf.size", Size); Notify(); }
    // spessore e corsivo non si ricordano: a ogni apertura (e ricollegamento) della cartella si riparte dal Regular dritto
    // cambiare spessore o corsivo per tutto il catalogo vale anche per le righe scelte a mano con la loro tendina:
    // la scelta di riga resta solo finché non si tocca di nuovo l'impostazione generale
    public Task SetWeightAsync(int weight) { Weight = weight; CatalogPicks.Clear(); Notify(); return Task.CompletedTask; }
    public Task SetItalicAsync(bool italic) { Italic = italic; CatalogPicks.Clear(); Notify(); return Task.CompletedTask; }
    public async Task SetTrackingAsync(int tracking) { Tracking = tracking; await SetAsync("pf.track", Tracking); Notify(); }
    public async Task SetQueryAsync(string query) { Query = query; await SetAsync("pf.query", Query); Notify(); }


    /// <summary>
    /// Ogni cartella caricata parte pulita: pangramma, 48 pt, spaziatura 0, nessun filtro, nessuna ricerca, senza i font Web.
    /// </summary>
    /// <summary>
    /// Stessa cartella, ricollegata? Non basta il nome (molte si chiamano "Fonts"): almeno metà dei file dev'essere la stessa.
    /// </summary>
    private static bool MostlySameFiles(IReadOnlyCollection<FontFile> before, IReadOnlyCollection<FontFile> now)
    {
        if (before.Count == 0 || now.Count == 0) return false;
        var paths = before.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        int common = now.Count(f => paths.Contains(f.Path));
        return common * 2 >= Math.Max(before.Count, now.Count);
    }

    private async Task ResetProofAsync()
    {
        SampleText = Presets["Pangram"];
        Size = 48;
        Tracking = 0;
        Filters = FontTrait.None;
        Query = "";
        WebOnly = false;
        CatalogPicks.Clear();
        await SetAsync("pf.text", SampleText);
        await SetAsync("pf.size", Size);
        await SetAsync("pf.filters", Filters);
        await SetAsync("pf.track", Tracking);
        await SetAsync("pf.query", Query);
        await SetAsync("pf.webonly", WebOnly);
    }
    /// <summary>Cambia l'aspetto subito (attributo data-theme sulla pagina) e lo ricorda per i prossimi avvii.</summary>
    public async Task SetThemeAsync(string theme)
    {
        Theme = theme is "light" or "dark" ? theme : "auto";
        await bridge.SetThemeAsync(Theme);
        await SetAsync("pf.theme", Theme);
        Notify();
    }

    public Task SetGridViewAsync(bool grid) { GridView = grid; Notify(); return Task.CompletedTask; }

    /// <summary>
    /// Nella pagina di uno stile: true sulla scheda dei caratteri, false sulla prova.
    /// Serve ai comandi in alto per sapere quali hanno effetto (vedi App: StyleOff, TrackOff).
    /// </summary>
    public bool StyleGlyphsTab { get; private set; } = true;

    public void SetStyleTab(bool glyphs)
    {
        if (StyleGlyphsTab == glyphs) return;
        StyleGlyphsTab = glyphs;
        Notify();
    }


    /// <summary>
    /// Gli stili di una variante che rispondono ai filtri per caratteristica e, con Italic acceso, i corsivi
    /// (compresi i variabili con il corsivo dentro): servono ai conteggi e alle varianti della colonna di sinistra.
    /// </summary>
    public IEnumerable<FontStyle> ShownStyles(TypeFamily f, string variant) =>
        f.Variants[variant].Where(s => (Filters == FontTrait.None || (s.Traits(f.Name, variant) & Filters) == Filters) && (!Italic || s.HasItalic));

    /// <summary>Quanti stili della famiglia rispondono ai filtri e al corsivo (tutti, se non ce ne sono).</summary>
    public int ShownCount(TypeFamily f)
    {
        // i conteggi si rifanno solo quando cambia qualcosa che li riguarda, non a ogni aggiornamento della pagina
        var key = (Families, Filters, Italic, infoStamp);
        if (!key.Equals(_countKey)) { _counts.Clear(); _countKey = key; }
        if (!_counts.TryGetValue(f, out var n)) _counts[f] = n = f.VariantNames(false).Sum(v => ShownStyles(f, v).Count());
        return n;
    }

    private readonly Dictionary<TypeFamily, int> _counts = [];
    private object? _countKey;
    private int infoStamp;   // cresce quando si scopre qualcosa dei file che cambia i conteggi (il corsivo dentro un variabile)

    public IEnumerable<TypeFamily> VisibleFamilies() =>
        SearchedFamilies().Where(f => (Filters == FontTrait.None || (f.Traits() & Filters) == Filters) && MatchesItalic(f));

    /// <summary>Famiglie che rispondono alla ricerca, prima dei filtri per caratteristica.</summary>
    public IEnumerable<TypeFamily> SearchedFamilies() => SearchedFamilies(Families);

    private IEnumerable<TypeFamily> SearchedFamilies(Dictionary<string, TypeFamily> source)
    {
        var q = Query.Trim();
        return source.Values
            .Where(f => f.Count(false) > 0 && (q.Length == 0
                || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || f.Variants.Keys.Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(f => f.Name, StringComparer.InvariantCultureIgnoreCase);
    }

    public async Task ToggleFilterAsync(FontTrait t)
    {
        Filters ^= t;
        await SetAsync("pf.filters", Filters);
        Notify();
    }

    /// <summary>I comandi della seconda riga (corpo, spessore, corsivo, spaziatura, colori) non sono più quelli di partenza.</summary>
    public bool ProofControlsChanged => Size != 48 || Weight != 400 || Italic || Tracking != 0 || InkColor is not null || PaperColor is not null;

    /// <summary>"Clear" della seconda riga: corpo, spessore, corsivo, spaziatura e colori tornano quelli di partenza.</summary>
    public async Task ClearProofControlsAsync()
    {
        Size = 48;
        Weight = 400;
        Italic = false;
        Tracking = 0;
        InkColor = PaperColor = null;
        CatalogPicks.Clear();
        await SetAsync("pf.size", Size);
        await SetAsync("pf.track", Tracking);
        Notify();
    }

    public async Task ClearFiltersAsync()
    {
        Filters = FontTrait.None;
        if (WebOnly) { WebOnly = false; CatalogPicks.Clear(); await SetAsync("pf.webonly", false); }
        await SetAsync("pf.filters", Filters);
        Notify();
    }

    /// <summary>Quanti file della cartella hanno già i dati per la classificazione.</summary>
    public bool ClassificationIsPartial =>
        !IsDemo && FilesToAnalyze().Any(f => !Analysis.TryGetValue(f.Path, out var a) || a.Schema < FontInfo.CurrentSchema);

    // ---- pagine "di servizio" (impostazioni, crediti, segnalazioni): si aprono sopra la pagina in cui si era
    //      e si chiudono tornando lì; intanto il catalogo (filtri, elenco dei font) non si vede ----

    /// <summary>Impostazioni, crediti o segnalazioni: le pagine senza catalogo.</summary>
    public bool IsSidePage => View is View.Settings or View.Credits or View.Feedback;

    /// <summary>La pagina da cui si è entrati nelle pagine di servizio (per "Back to…").</summary>
    public View ViewBeforeSettings { get; private set; } = View.Catalog;

    /// <summary>Nella pagina Feedback: segnalazione di un problema o proposta.</summary>
    public FeedbackKind Feedback { get; private set; } = FeedbackKind.Bug;

    public void OpenPage(View page)
    {
        if (!IsSidePage) ViewBeforeSettings = View;
        View = page;
        Navigated();
    }

    public void OpenSettings() => OpenPage(View.Settings);

    public void OpenFeedback(FeedbackKind kind)
    {
        Feedback = kind;
        OpenPage(View.Feedback);
    }

    public void CloseSettings()
    {
        if (!IsSidePage) return;
        View = ViewBeforeSettings switch
        {
            View.Style when CurrentStyle is null => View.Family,
            View.Settings or View.Credits or View.Feedback => View.Catalog,
            var v => v,
        };
        Navigated();
    }

    /// <summary>Esc nella pagina delle impostazioni (vedi bfontb.js): si torna indietro.</summary>
    [JSInvokable]
    public void OnEscape() => CloseSettings();

    /// <param name="anchor">La sezione di una variante a cui scendere nella pagina della famiglia (vedi FamilyView).</param>
    public void OpenFamily(string name, string? anchor = null) { View = View.Family; CurrentFamily = name; JumpTo = anchor; Navigated(); }

    /// <summary>Lo stile scelto con la tendina di una riga del catalogo, per famiglia e variante: vale fino al prossimo
    /// cambio di spessore o corsivo generale (SetWeightAsync, SetItalicAsync) o fino alla prossima cartella.</summary>
    public Dictionary<string, string> CatalogPicks { get; } = [];

    /// <summary>La sezione della pagina della famiglia a cui scendere appena si apre (poi si azzera).</summary>
    public string? JumpTo { get; set; }

    /// <summary>Torna al catalogo (dal percorso, dal pulsante "Back to the catalog", dal secondo clic su una famiglia).</summary>
    public void ShowCatalog() { View = View.Catalog; Navigated(); }

    /// <summary>Dopo ogni cambio di vista voluto dall'utente: l'indirizzo atteso da un link non serve più.</summary>
    private void Navigated() { pendingUrl = null; Notify(); }

    // ---- indirizzi: ogni vista ha il suo, la pagina non si ricarica (lo aggiorna App, vedi SyncUrlAsync) ----
    //      /app                              catalogo
    //      /app/family/abc-marfa             pagina della famiglia
    //      /app/family/abc-marfa/mono-bold   pagina di uno stile (variante + stile, senza il nome della famiglia)
    //      /app/settings · /app/credits · /app/report · /app/suggest

    public const string AppBase = "/app";
    private const string AppName = "B(fore) Font B(ook)";

    /// <summary>L'indirizzo che il browser mostra in questo momento.</summary>
    public string ShownUrl { get; set; } = AppBase;

    /// <summary>Il prossimo aggiornamento dell'indirizzo lo sostituisce invece di aggiungere un passo alla cronologia.</summary>
    public bool ReplaceUrl { get; set; }

    /// <summary>
    /// Un indirizzo che non si può ancora mostrare (una famiglia, ma la cartella non è aperta): resta nella barra
    /// e si riprova quando arriva la cartella.
    /// </summary>
    private string? pendingUrl;

    /// <summary>L'indirizzo della vista corrente.</summary>
    public string UrlFor()
    {
        if (pendingUrl is not null) return pendingUrl;
        return View switch
        {
            View.Family when CurrentFamily is { } f => $"{AppBase}/family/{Uri.EscapeDataString(Slug(f))}",
            View.Style when CurrentStyle is { } r => $"{AppBase}/family/{Uri.EscapeDataString(Slug(r.Family.Name))}/{Uri.EscapeDataString(StyleSlug(r))}",
            View.Settings => AppBase + "/settings",
            View.Credits => AppBase + "/credits",
            View.Feedback => AppBase + (Feedback == FeedbackKind.Bug ? "/report" : "/suggest"),
            _ => AppBase,
        };
    }

    /// <summary>Il titolo della scheda del browser (e della cronologia) per la vista corrente.</summary>
    public string TitleFor()
    {
        if (IsDemo && !IsSidePage) return AppName;
        var what = View switch
        {
            View.Family when CurrentFamily is { } f => f,
            View.Style when CurrentStyle is { } r => $"{(r.Variant == r.Family.Name ? r.Family.Name : r.Variant)} {r.Style.Label}",
            View.Settings => "Settings",
            View.Credits => "Credits",
            View.Feedback => Feedback == FeedbackKind.Bug ? "Report a bug" : "Suggest a feature",
            _ => null,
        };
        return what is null ? AppName : $"{what} — {AppName}";
    }

    /// <summary>Porta l'app nella vista di un indirizzo (all'apertura, con Indietro e Avanti del browser). Non avvisa: lo fa chi chiama.</summary>
    public void ApplyUrl(string path)
    {
        pendingUrl = null;
        var parts = path.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        var rest = parts.Length > 0 && parts[0] == AppBase.Trim('/') ? parts[1..] : [];
        switch (rest)
        {
            case ["settings"]: EnterSidePage(View.Settings); return;
            case ["credits"]: EnterSidePage(View.Credits); return;
            case ["report"]: Feedback = FeedbackKind.Bug; EnterSidePage(View.Feedback); return;
            case ["suggest"]: Feedback = FeedbackKind.Feature; EnterSidePage(View.Feedback); return;
            case ["family", var fam, ..]:
                // senza cartella (o prima che arrivi) la famiglia non c'è ancora: si aspetta
                var family = IsDemo ? null : Families.Values.FirstOrDefault(f => Slug(f.Name) == fam);
                if (family is null) { View = View.Catalog; if (IsDemo) pendingUrl = path; return; }
                CurrentFamily = family.Name;
                var style = rest.Length > 2 ? family.Variants.SelectMany(kv => kv.Value.Select(st => new StyleRef(st, family, kv.Key)))
                    .FirstOrDefault(r => StyleSlug(r) == rest[2]) : null;
                if (style is null) View = View.Family;
                else { View = View.Style; CurrentStylePath = style.Style.Path; }
                return;
            default: View = View.Catalog; return;
        }
    }

    private void EnterSidePage(View page)
    {
        if (!IsSidePage) ViewBeforeSettings = View;
        View = page;
    }

    /// <summary>Indietro o Avanti nel browser (vedi bfontb.js, router).</summary>
    [JSInvokable]
    public void OnUrl(string path)
    {
        ShownUrl = path;
        ApplyUrl(path);
        ReplaceUrl = true;   // se l'indirizzo non era quello giusto (una famiglia sparita), lo si corregge senza aggiungere passi
        Notify();
    }

    /// <summary>"ABC Marfa" → "abc-marfa": lettere senza accenti e cifre, il resto diventa un trattino.</summary>
    public static string Slug(string text)
    {
        var plain = new System.Text.StringBuilder();
        foreach (var c in text.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        var slug = System.Text.RegularExpressions.Regex.Replace(plain.ToString(), "-+", "-").Trim('-');
        return slug.Length == 0 ? "untitled" : slug;
    }

    /// <summary>Lo stile nell'indirizzo: variante e stile senza il nome della famiglia ("ABC Marfa Mono" Bold → "mono-bold").</summary>
    private static string StyleSlug(StyleRef r)
    {
        var slug = Slug(r.Variant == r.Family.Name ? r.Style.Label : $"{r.Variant} {r.Style.Label}");
        var fam = Slug(r.Family.Name) + "-";
        return slug.StartsWith(fam, StringComparison.Ordinal) && slug.Length > fam.Length ? slug[fam.Length..] : slug;
    }

    // ---- apertura di una cartella (chiamato dal JavaScript dopo la scelta o il trascinamento) ----
    public sealed record FileEntryDto(int Id, string Path, long Size);

    [JSInvokable]
    public async Task OnFolderListed(FileEntryDto[] entries)
    {
        StatusMessage = "Reading the file list…";
        Notify();
        loader.Reset();
        var files = entries.Select(e => new FontFile(e.Id, e.Path, e.Size))
            .Where(f => (f.IsDesktop || f.IsWeb) && !SkipDir(f.Path))
            .ToList();
        glyphs.Reset();
        if (files.Count == 0)
        {
            IsDemo = true;
            Connected = false;
            Families = DemoFonts.Create();
            StatusMessage = "No fonts found in this folder";
            Notify();
            return;
        }
        var root = files[0].Segments[0];
        bool sameFolder = !IsDemo && root == RootName && MostlySameFiles(Files, files);   // ricollegata dopo un ricaricamento: restiamo dove eravamo
        if (!sameFolder) await ResetProofAsync();   // cartella nuova: si riparte puliti; la stessa ricollegata: tutto resta com'era
        Weight = 400;    // tranne spessore e corsivo del catalogo: sempre Regular dritto (ce l'hanno quasi tutte le famiglie)
        Italic = false;
        InkColor = PaperColor = null;   // e i colori: quelli dell'interfaccia
        var keepView = View;
        var keepFamily = CurrentFamily;
        var keepStyle = CurrentStylePath;
        Files = files;
        StatusMessage = null;
        await OpenFilesAsync(root, connected: true);
        if (sameFolder) { View = keepView; CurrentFamily = keepFamily; CurrentStylePath = keepStyle; }
        if (pendingUrl is { } wanted) ApplyUrl(wanted);   // arrivati da un link a una famiglia: ora la si può aprire
        if (View == View.Style && CurrentStyle is null) View = View.Family;
        await SetAsync("pf.lastRoot", RootName);
        await cache.SaveListingAsync(RootName, Files);
        Notify();
    }

    /// <summary>Carica preferenze, analisi e correzioni della cartella e costruisce le famiglie.</summary>
    private async Task OpenFilesAsync(string root, bool connected)
    {
        IsDemo = false;
        Connected = connected;
        RootName = root;
        Analysis = new(StringComparer.Ordinal);
        Proposal = null;
        Fix = await GetAsync("pf.fix." + RootName, new Corrections());
        Fix.Exclude.Clear();   // l'esclusione delle cartelle non c'è più: le cartelle escluse in passato tornano visibili
        Mode = await GetAsync("pf.mode." + RootName, OrgMode.Folders);
        AnalyzeWeb = await GetAsync("pf.anweb." + RootName, false);

        var cached = await cache.LoadAsync(RootName + "/");
        foreach (var f in Files) if (cached.TryGetValue(f.Path, out var info)) Analysis[f.Path] = info;
        if (Analysis.Count > 0) ComputeProposal();
        if (Mode == OrgMode.Analysis && Proposal is null) Mode = OrgMode.Folders;
        Rebuild();
        CurrentFamily = null;
        CurrentStylePath = null;
        // cartella disordinata: si aprono le impostazioni (preparazione), e "indietro" porta al catalogo
        ViewBeforeSettings = View.Catalog;
        View = Mode == OrgMode.Folders && !FamilyOrganizer.StructureLooksReady(Files) ? View.Settings : View.Catalog;
    }

    private static bool SkipDir(string path) =>
        path.Split('/').Any(s => s is "_duplicates" or "_organise_log" or "_doppioni da eliminare" or "_riordino_log" or "_cartelle vuote");

    // ---- organizzazione ----
    public void ComputeProposal() => Proposal = FamilyOrganizer.ComputeProposal(Files, Analysis, Fix);

    public void Rebuild()
    {
        if (IsDemo) return;
        Families = FamilyOrganizer.Build(Files, Mode, Proposal, Analysis, Fix);
    }

    public async Task SetModeAsync(OrgMode mode)
    {
        Mode = mode;
        await SetAsync("pf.mode." + RootName, Mode);
        Rebuild();
        Notify();
    }

    public async Task SetAnalyzeWebAsync(bool value)
    {
        AnalyzeWeb = value;
        await SetAsync("pf.anweb." + RootName, value);
        Notify();
    }

    /// <summary>Dopo una correzione: ricalcola la proposta, salva e aggiorna il catalogo.</summary>
    public async Task ApplyChangesAsync()
    {
        ComputeProposal();
        await SetAsync("pf.fix." + RootName, Fix);
        Rebuild();
        Notify();
    }

    public async Task ForgetAnalysisAsync()
    {
        await cache.ClearAsync(RootName + "/");
        Analysis = new(StringComparer.Ordinal);
        Proposal = null;
        Mode = OrgMode.Folders;
        await SetAsync("pf.mode." + RootName, Mode);
        Rebuild();
        Notify();
    }

    /// <summary>Il file va (ri)letto: mai analizzato, o analizzato da una versione dell'app senza classificazione.</summary>
    public bool NeedsAnalysis(FontFile f) => !Analysis.TryGetValue(f.Path, out var a) || a.Schema < FontInfo.CurrentSchema;

    public IEnumerable<FontFile> FilesToAnalyze() =>
        Files.Where(f => (f.IsDesktop || (AnalyzeWeb && f.IsWeb)) && !Fix.IsExcluded(f.Path));

    /// <summary>Chiamato dall'analisi quando ha finito (o è stata fermata).</summary>
    public async Task AnalysisFinishedAsync(bool completed)
    {
        ComputeProposal();
        if (completed) { Mode = OrgMode.Analysis; await SetAsync("pf.mode." + RootName, Mode); }
        Rebuild();
        Notify();
    }

    // ---- preferenze nel browser ----
    private static readonly JsonSerializerOptions Json = new();

    private async Task<T> GetAsync<T>(string key, T fallback)
    {
        try
        {
            var raw = await bridge.GetLocalAsync(key);
            return raw is null ? fallback : JsonSerializer.Deserialize<T>(raw, Json) ?? fallback;
        }
        catch { return fallback; }
    }

    private async Task SetAsync<T>(string key, T value)
    {
        try { await bridge.SetLocalAsync(key, JsonSerializer.Serialize(value, Json)); } catch { }
    }
}
