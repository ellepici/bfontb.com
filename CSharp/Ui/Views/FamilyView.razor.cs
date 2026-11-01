using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

public partial class FamilyView : IDisposable
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private FontLoader Loader { get; set; } = default!;
    // i salti alle varianti si fanno a mano: con <base href="/"> un link "#…" porterebbe all'indirizzo "/" (la presentazione)
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    private sealed record VariantGroup(string Name, string Anchor, List<FontStyle> Styles);

    private TypeFamily? Current;
    private List<VariantGroup> Variants = [];

    // accanto al nome della famiglia solo gli autori (disegnatori e fonderie), tutti quelli diversi che ci sono nelle sue varianti
    private string Authors = "";
    private List<string> Traits = [];

    /// <summary>Con un filtro attivo si vedono solo le varianti che rispondono; l'utente può chiedere di vederle tutte.</summary>
    private bool showAll;
    private string? showAllFor;
    private int HiddenVariants;
    private string FilterLabel = "";

    protected override void OnParametersSet() => Compute();

    // i crediti arrivano man mano che si leggono i file: quando sono di questa famiglia si rifà il confronto (una volta ogni tanto)
    protected override void OnInitialized() => Loader.InfoRead += OnInfoRead;
    public void Dispose() => Loader.InfoRead -= OnInfoRead;

    private bool recheckQueued;
    private void OnInfoRead(FontStyle st)
    {
        if (recheckQueued || Current is null || !Current.Variants.Values.Any(l => l.Contains(st))) return;
        recheckQueued = true;
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(150);
            recheckQueued = false;
            if (Current is null) return;
            Compute();
            StateHasChanged();
        });
    }

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
        var names = Current.VariantNames(false).ToList();
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
        // i crediti stanno solo qui, accanto al nome della famiglia (anche se cambiano da una variante all'altra):
        // non accanto alle varianti né nelle righe. Si guardano gli stili già letti; gli altri arrivano man mano
        var all = Variants.SelectMany(v => v.Styles).Select(s => s.Info).OfType<FontInfo>().ToList();
        Authors = Join(AuthorsOf(all), YearsOf(all));
    }

    /// <summary>
    /// Tutti gli autori della famiglia, ognuno una volta sola: i disegnatori (anche se in un font sono scritti insieme ad altri)
    /// e poi le fonderie. "by Johannes Breyer, Fabian Harb, Arnaud Chemin · Dinamo", non due righe quasi uguali.
    /// Un disegnatore che è anche la fonderia ("by Dinamo · Dinamo") resta solo come fonderia.
    /// </summary>
    private static string AuthorsOf(IEnumerable<FontInfo> infos)
    {
        var designers = new List<string>();
        var foundries = new List<string>();
        static void Add(List<string> list, string? name)
        {
            name = name?.Trim().TrimEnd('.', ',');
            if (!string.IsNullOrEmpty(name) && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        }
        foreach (var i in infos)
        {
            foreach (var d in (i.Designer ?? "").Split([",", ";", "/", " & ", " and ", " und ", " e "], StringSplitOptions.RemoveEmptyEntries))
                Add(designers, d);
            Add(foundries, i.FoundryName);
        }
        designers.RemoveAll(d => foundries.Contains(d, StringComparer.OrdinalIgnoreCase));
        var parts = new List<string>();
        if (designers.Count > 0) parts.Add("by " + string.Join(", ", designers));
        if (foundries.Count > 0) parts.Add(string.Join(", ", foundries));
        return Join([.. parts]);
    }

    // autori, fonderie e anni separati dal punto in mezzo: "by Johannes Breyer · Dinamo · 2021"
    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(x => x.Length > 0));

    /// <summary>Gli anni degli stili, dal primo all'ultimo: "2020–2023", o uno solo ("2023"). Anche dagli anni scritti come intervallo.</summary>
    private static string YearsOf(IEnumerable<FontInfo> infos)
    {
        var years = infos.SelectMany(i => System.Text.RegularExpressions.Regex.Matches(i.Year ?? "", @"\b(1[5-9]\d\d|20\d\d)\b"))
            .Select(m => int.Parse(m.Value)).ToList();
        if (years.Count == 0) return "";
        int min = years.Min(), max = years.Max();
        return min == max ? $"{min}" : $"{min}–{max}";
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
    // i font variabili dicono tutto l'intervallo del peso ("weight 100–900"), gli altri il loro valore
    private static string WeightNote(FontStyle s) =>
        (s.Info is { WghtMin: { } lo, WghtMax: { } hi } ? $"weight {lo:0}–{hi:0}" : $"weight ~{s.Weight}") + (StyleNames.EnglishWeight(s.Label, s.Weight) is { } en ? $" · {en}" : "") + ItalicNote(s);

    private static string CardNote(FontStyle s) =>
        $"~{s.Weight}" + (StyleNames.EnglishWeight(s.Label, s.Weight) is { } en ? $" · {en}" : "") + ItalicNote(s);

    // "· italic" solo se il nome dello stile non lo dice già ("Medium Italic", "Oblique", "Kursiv"…: sarebbe una ripetizione)
    private static readonly string[] ItalicWords = ["italic", "oblique", "slant", "inclined", "kursiv", "cursiv", "corsiv"];
    private static string ItalicNote(FontStyle s) =>
        s.Italic && !ItalicWords.Any(w => s.Label.Contains(w, StringComparison.OrdinalIgnoreCase)) ? " · italic" : "";

    // ---- griglia: come quella del catalogo, sei schede per riga (meno solo se la colonna è stretta, mai sotto i 180px);
    //      il corpo scelto in alto cambia solo quanto è grande «Aa» dentro la scheda, non quante schede stanno in una riga ----
    private const int Gap = 16;
    private static string GridStyle => $"--gap:{Gap}px";
    private double AaShare => Math.Clamp(0.18 + Lib.Size / 400.0, 0.24, 0.45);   // la stessa proporzione del catalogo
}
