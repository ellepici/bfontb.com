using System.Globalization;
using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>
/// Un blocco della griglia dei caratteri. Si ridisegna solo se cambia il blocco, il font o la selezione
/// dentro questo blocco: con migliaia di caratteri ridisegnare tutto a ogni clic sarebbe lento.
/// </summary>
public partial class GlyphGroupView
{
    /// <summary>Oltre questo numero di caratteri il blocco si apre con un pulsante (font con decine di migliaia di ideogrammi).</summary>
    private const int Limit = 1200;

    [Parameter, EditorRequired] public GlyphGroup Group { get; set; } = default!;
    [Parameter, EditorRequired] public string Css { get; set; } = "";
    [Parameter] public int SelectedId { get; set; } = -1;
    [Parameter] public EventCallback<GlyphItem> OnPick { get; set; }

    /// <summary>Il font per misurare i segni combinanti con il canvas: inizio della proprietà CSS font (es. "normal 400") e famiglia.</summary>
    [Parameter] public string FontPrefix { get; set; } = "normal 400";
    [Parameter] public string FontFamily { get; set; } = "";

    /// <summary>Il disegno di un elemento letto dal file (vedi GlyphSet.Ink).</summary>
    [Parameter] public Func<GlyphItem, InkBox?>? Ink { get; set; }

    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>
    /// Forme alternative disegnate fuori dalla loro larghezza (gli accenti per le maiuscole di 'case', larghi zero):
    /// lo spostamento che porta il disegno al centro della cella, come per gli accenti codificati.
    /// Gli altri caratteri restano centrati sulla larghezza, come nel resto della griglia.
    /// </summary>
    private string? FeatureShift(GlyphItem it)
    {
        if (it.Feature is null || Ink?.Invoke(it) is not { } k) return null;
        bool outside = k.Advance < 0.02 || k.Center < 0 || k.Center > k.Advance;
        if (!outside) return null;
        return (k.Advance / 2 - k.Center).ToString("0.####", CultureInfo.InvariantCulture) + "em";
    }

    private ElementReference cellsEl;
    private bool recenter = true;

    /// <summary>
    /// Un segno combinante (accento da solo) è disegnato fuori dalla sua larghezza, che è zero:
    /// centrato come gli altri caratteri finirebbe spostato. Lo centra il JavaScript misurandone il disegno.
    /// </summary>
    private static bool IsMark(GlyphItem it) => it.CodePoint is int cp && it.Feature is null
        && CharUnicodeInfo.GetUnicodeCategory(cp) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!recenter) return;
        recenter = false;
        if (FontFamily.Length == 0 || !Group.Items.Any(IsMark)) return;
        try { await Bridge.CenterMarksAsync(cellsEl, FontPrefix, FontFamily); } catch { /* restano centrati sulla larghezza */ }
    }

    private bool expanded;
    private GlyphGroup? lastGroup;
    private string? lastCss;
    private int lastSel = -2;

    private int MySel => Group.Items.Count > 0 && SelectedId >= Group.Items[0].Id && SelectedId <= Group.Items[^1].Id ? SelectedId : -1;

    protected override bool ShouldRender()
    {
        int sel = MySel;
        if (ReferenceEquals(lastGroup, Group) && lastCss == Css && lastSel == sel) return false;
        if (!ReferenceEquals(lastGroup, Group)) expanded = false;
        recenter = true;
        lastGroup = Group;
        lastCss = Css;
        lastSel = sel;
        return true;
    }

    private IEnumerable<GlyphItem> Shown => expanded ? Group.Items : Group.Items.Take(Limit);

    private void Expand()
    {
        expanded = true;
        lastSel = -2;   // forza il ridisegno
    }

    private string CellStyle(GlyphItem it) => it.Feature is null ? Css : $"{Css};font-feature-settings:{it.Feature}";

    private static string Title(GlyphItem it) => it.CodePoint is int cp ? $"U+{cp:X4}" : $"{it.Text} with {it.Feature}";
}
