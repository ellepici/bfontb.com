using System.Globalization;
using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Bfontb.Ui.Parts;

/// <summary>
/// Tutti i caratteri di uno stile: a sinistra il carattere scelto, grande, con le linee di riferimento
/// (ascendente, altezza delle maiuscole, altezza x, linea di base, discendente); a destra la griglia divisa per blocchi.
/// </summary>
public partial class GlyphExplorer
{
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    [Parameter, EditorRequired] public FontStyle Style { get; set; } = default!;

    /// <summary>I caratteri letti dal file; null per i caratteri di esempio (si mostra un alfabeto di base).</summary>
    [Parameter] public GlyphSet? Set { get; set; }

    private sealed record Line(string Label, double Y, bool Strong, bool Right = false);

    private GlyphSet? shownSet;
    private FontStyle? shownStyle;
    private GlyphSet Data = new();
    private GlyphItem? Sel;
    private Dictionary<int, GlyphGroup> GroupOf = [];

    private ElementReference bigText, gxEl;
    private bool revealSel;   // dopo una freccia: la cella scelta deve vedersi
    private (GlyphItem? Item, double Size) centered;

    /// <summary>
    /// Centra il carattere grande sul suo disegno reale e non sulla sua larghezza teorica: le legature
    /// (per esempio [10] che diventa un 10 cerchiato) e i glifi con margini laterali diversi finirebbero spostati.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // frecce su e giù: le gestisce bfontb.js (sa com'è disposta la griglia); destra e sinistra le gestisce OnKey
        if (firstRender) { try { await Bridge.GlyphKeysAsync(gxEl); } catch { } }
        if (revealSel) { revealSel = false; try { await Bridge.RevealGlyphAsync(gxEl); } catch { } }
        if (Sel is null || (ReferenceEquals(centered.Item, Sel) && centered.Size == FontSize)) return;
        centered = (Sel, FontSize);
        var family = CanvasFamily;
        var prefix = CanvasPrefix;
        // le forme alternative si centrano sul disegno letto dal file: il canvas non sa accendere le funzioni OpenType
        double? inkCenter = Sel.Feature is null ? null : Data.Ink(Sel)?.Center;
        try { await Bridge.CenterGlyphAsync(bigText, Sel.Text, prefix, family, FontSize, Sel.Feature, inkCenter); }
        catch { /* resta centrato sulla larghezza: va bene lo stesso */ }
    }

    // misure del disegno grande (viewBox 1000 × 1000)
    private double FontSize, Baseline;
    private List<Line> Lines = [];

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(shownSet, Set) && ReferenceEquals(shownStyle, Style) && Data.All.Count > 0) return;
        shownSet = Set;
        shownStyle = Style;
        Data = Set ?? Basic();
        GroupOf = Data.Blocks.Concat(Data.Features).SelectMany(g => g.Items.Select(i => (i.Id, g))).ToDictionary(x => x.Id, x => x.g);
        Sel = Data.All.FirstOrDefault(i => i.CodePoint == 'A') ?? Data.All.FirstOrDefault();
        Measure(Data.Metrics);
    }

    /// <summary>Calcola corpo e linee perché tutto il carattere, da ascendente a discendente, stia nel riquadro.</summary>
    private void Measure(GlyphMetrics? m)
    {
        Lines = [];
        var mm = m ?? new GlyphMetrics(1000, 800, -200, null, null);
        double span = mm.Ascender - mm.Descender;
        double k = 600 / span;                 // pixel del riquadro per unità del font
        FontSize = mm.UnitsPerEm * k;
        Baseline = 200 + mm.Ascender * k;
        if (m is null) { Lines.Add(new Line("Baseline", Baseline, true)); return; }
        Lines.Add(new Line("Ascender", Baseline - m.Ascender * k, false));
        if (m.CapHeight is int cap) Lines.Add(new Line("Cap height", Baseline - cap * k, false));
        if (m.XHeight is int x) Lines.Add(new Line("x-height", Baseline - x * k, false));
        Lines.Add(new Line("Baseline", Baseline, true));
        Lines.Add(new Line("Descender", Baseline - m.Descender * k, false));
        // linee vicine (per esempio ascendente e altezza delle maiuscole): l'etichetta della seconda va a destra
        Lines = Lines.OrderBy(l => l.Y).ToList();
        for (int i = 1; i < Lines.Count; i++)
            if (Lines[i].Y - Lines[i - 1].Y < 40 && !Lines[i - 1].Right) Lines[i] = Lines[i] with { Right = true };
    }

    private void Pick(GlyphItem it) => Sel = it;

    private void Step(int d)
    {
        if (Sel is null || Data.All.Count == 0) return;
        int i = (Sel.Id + d + Data.All.Count) % Data.All.Count;
        Sel = Data.All[i];
    }

    private void OnKey(KeyboardEventArgs e)
    {
        if (e.Key == "ArrowRight") { Step(1); revealSel = true; }
        else if (e.Key == "ArrowLeft") { Step(-1); revealSel = true; }
    }

    private string Css => Style.Css;

    // il font come lo vuole il canvas (per misurare il disegno dei caratteri)
    private string CanvasFamily => Style.IsDemo ? Style.DemoCssFamily! : $"\"{Style.FaceName}\"";
    private string CanvasPrefix => Style.IsDemo ? $"{(Style.Italic ? "italic " : "")}{Style.Weight}" : "normal 400";

    // posizione e corpo nelle unità del riquadro 1000 × 1000 (--u, vedi style-page.css): restano giusti se il riquadro cambia misura
    private string BigStyle => $"{Css};font-size:calc(var(--u) * {F(FontSize)});left:calc(var(--u) * 500);top:calc(var(--u) * {F(Baseline)})"
        + (Sel?.Feature is { } f ? $";font-feature-settings:{f}" : "");

    private string SelCode => Sel?.CodePoint is int cp
        ? $"U+{cp:X4}"
        : Sel?.Feature is { } f ? $"“{Sel.Text}” with {FeatureName(f)}" : "";

    /// <summary>'salt' 2 → salt 2; 'liga' 1 → liga.</summary>
    private static string FeatureName(string css)
    {
        var parts = css.Replace("'", "").Split(' ');
        return parts.Length == 2 && parts[1] != "1" ? $"{parts[0]} {parts[1]}" : parts[0];
    }

    private string SelGroup => Sel is not null && GroupOf.TryGetValue(Sel.Id, out var g) ? g.Title : "";

    private string SelCategory => Sel?.CodePoint is int cp ? Category(cp) : "alternate form";

    private static string Category(int cp) => CharUnicodeInfo.GetUnicodeCategory(cp) switch
    {
        UnicodeCategory.UppercaseLetter => "uppercase letter",
        UnicodeCategory.LowercaseLetter => "lowercase letter",
        UnicodeCategory.TitlecaseLetter => "letter",
        UnicodeCategory.ModifierLetter => "modifier letter",
        UnicodeCategory.OtherLetter => "letter",
        UnicodeCategory.DecimalDigitNumber => "digit",
        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => "number",
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark => "combining mark",
        UnicodeCategory.CurrencySymbol => "currency symbol",
        UnicodeCategory.MathSymbol => "math symbol",
        UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol => "symbol",
        UnicodeCategory.PrivateUse => "private use",
        _ => "punctuation",
    };

    /// <summary>Il file ha più glifi di quelli che si possono mostrare: gli altri escono solo con regole contestuali.</summary>
    private string? HiddenNote
    {
        get
        {
            if (Set is null || Data.TotalGlyphs == 0) return null;
            int hidden = Data.TotalGlyphs - Data.Reachable - 1;   // il glifo .notdef non si mostra mai
            return hidden > 10
                ? $"About {Fmt.N(hidden)} glyphs only appear in certain contexts (for example before or after other letters) and can’t be shown on their own."
                : null;
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>Per i caratteri di esempio, che non leggiamo dal file: latino di base e latino-1.</summary>
    private static GlyphSet Basic()
    {
        int id = 0;
        var all = new List<GlyphItem>();
        var blocks = new List<GlyphGroup>();
        foreach (var (key, name, from, to) in new[] { ("b0", "Basic Latin", 0x21, 0x7E), ("b80", "Latin-1 Supplement", 0xA1, 0xFF) })
        {
            var items = Enumerable.Range(from, to - from + 1).Where(c => c != 0xAD)
                .Select(c => new GlyphItem(id++, char.ConvertFromUtf32(c), c, null)).ToList();
            all.AddRange(items);
            blocks.Add(new GlyphGroup(key, name, null, items));
        }
        return new GlyphSet { Blocks = blocks, All = all };
    }
}
