using System.Globalization;
using System.Text.RegularExpressions;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Bfontb.Ui.Parts;

/// <summary>
/// Una tendina dei colori in alto, per il testo (Ink = true) o per lo sfondo dei testi di prova (non dell'interfaccia):
/// nella barra ce ne sono due, una accanto all'altra. I colori li tiene FontLibrary (InkColor, PaperColor);
/// App li passa al CSS come --spec-ink e --spec-paper.
/// </summary>
public partial class ColorMenu : IDisposable
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }
    /// <summary>true: il colore del testo; false: quello dello sfondo.</summary>
    [Parameter] public bool Ink { get; set; } = true;

    private string Label => Ink ? "Text" : "Background";
    private string? Current => Ink ? Lib.InkColor : Lib.PaperColor;

    private static readonly string[] Palette =
    [
        // una riga sola: i colori pronti più utili; gli altri si trovano con le barre e restano nei recenti
        "#111111", "#FFFFFF", "#8A8A8A", "#F5EEDC", "#FFE14D", "#C8312B", "#2A3FD6", "#2E8B57",
    ];

    private bool open;

    private Task Toggle() { if (open) return Close(); open = true; return Task.CompletedTask; }
    private Task OnKey(KeyboardEventArgs e) => e.Key == "Escape" ? Close() : Task.CompletedTask;

    private Task Close() { open = false; autoHex = null; return Task.CompletedTask; }   // si rilegge alla riapertura (il tema può cambiare)

    // un colore personalizzato (barre o codice) va subito nei recenti; uno della tavolozza no, è già lì
    private Task Remember(string c) => Palette.Contains(c, StringComparer.OrdinalIgnoreCase) ? Task.CompletedTask : Lib.RememberColorAsync(c);

    // un colore pronto (tavolozza, recenti, tema): si sceglie e basta
    private Task PickReady(string? c) { Lib.EndColorEdit(); return Pick(c); }

    private Task Pick(string? c) => Ink ? Lib.SetColorsAsync(c, Lib.PaperColor) : Lib.SetColorsAsync(Lib.InkColor, c);

    // ---- barre HSL (tinta, saturazione, luminosità), sempre aperte: le disegna e le muove bfontb.js (colorWheel) ----
    private ElementReference wheelEl;
    private string? wheelHex;  // il colore da cui le barre sono partite o che hanno mandato per ultimo
    private DotNetObjectReference<ColorMenu>? selfRef;

    // senza un colore scelto le barre partono dal colore del tema (letto dal CSS all'apertura: chiaro o scuro)
    private string WheelStart => Current ?? autoHex ?? (Ink ? "#000000" : "#F2F3EF");

    /// <summary>Il colore del tema (auto): nero o bianco pieno per il testo (--spec-auto-ink), la carta della pagina per lo sfondo.</summary>
    private string? autoHex;

    // il colore mostrato nei campi del codice: quello scelto o, in grigio, quello del tema
    private string? ShownHex => Current ?? autoHex;

    /// <summary>Nel campo esadecimale: le sei cifre, senza il # (è già davanti).</summary>
    private string HexValue => ShownHex?.TrimStart('#') ?? "";

    private static readonly string[] ChannelNames = ["red", "green", "blue"];

    /// <summary>Il valore di un canale (0 rosso, 1 verde, 2 blu), da 0 a 255.</summary>
    private string Channel(int i) => ShownHex is { } h ? int.Parse(h.Substring(1 + i * 2, 2), NumberStyles.HexNumber).ToString(CultureInfo.InvariantCulture) : "";

    // un canale scritto a mano (o cambiato con le frecce): gli altri due restano com'erano; vuoto vale 0
    private async Task OnChannel(int i, ChangeEventArgs e)
    {
        if (ShownHex is not { } h) return;
        var raw = (e.Value?.ToString() ?? "").Trim();
        if (raw.Length > 0 && !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return;
        var n = Math.Clamp(raw.Length == 0 ? 0 : int.Parse(raw, CultureInfo.InvariantCulture), 0, 255);
        var c = h[..(1 + i * 2)] + n.ToString("X2") + h[(3 + i * 2)..];
        if (string.Equals(c, Current, StringComparison.OrdinalIgnoreCase)) return;
        await Pick(c);
        await Remember(c);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!open) { wheelHex = null; return; }   // alla riapertura le barre sono nuove: si ridisegnano
        selfRef ??= DotNetObjectReference.Create(this);
        if (autoHex is null)
        {
            // il colore "auto" del tema in uso, per scriverlo nel campo e far partire le barre da lì
            try { autoHex = Normalize(await Bridge.CssColorAsync(Ink ? "--spec-auto-ink" : "--paper") ?? ""); } catch { }
            if (autoHex is not null) { StateHasChanged(); return; }
        }
        // si (ri)disegnano solo se il colore è cambiato da fuori (tavolozza, codice, scambio), non per quello mandato dalle barre
        var start = WheelStart;
        if (string.Equals(start, wheelHex, StringComparison.OrdinalIgnoreCase)) return;
        wheelHex = start;
        try { await Bridge.ColorWheelAsync(wheelEl, selfRef, start, Ink); } catch { }
    }

    [JSInvokable]
    public async Task OnWheel(string hex, bool isInk)
    {
        if (Normalize(hex) is not { } c) return;
        wheelHex = c;
        await Pick(c);
        await Remember(c);   // subito nei recenti (i ritocchi entro 6 secondi aggiornano lo stesso)
    }

    public void Dispose() => selfRef?.Dispose();

    // il codice esadecimale scritto a mano (vuoto: il colore del tema)
    private async Task OnCode(ChangeEventArgs e)
    {
        var raw = (e.Value?.ToString() ?? "").Trim();
        string? c = raw.Length == 0 ? null : (Normalize(raw) ?? FromRgb(raw));
        if (raw.Length > 0 && c is null) return;   // codice non valido: si lascia com'era
        if (c is null) { await PickReady(null); return; }
        await Pick(c);
        await Remember(c);
    }

    /// <summary>"232, 93, 158", "232 93 158" o "rgb(232, 93, 158)" → "#E85D9E"; null se non è un colore RGB.</summary>
    private static string? FromRgb(string s)
    {
        var m = Regex.Match(s, @"^\s*(?:rgb\s*\(\s*)?(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*[,\s]\s*(\d{1,3})\s*\)?\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = Enumerable.Range(1, 3).Select(i => int.Parse(m.Groups[i].Value)).ToArray();
        return v.Any(x => x > 255) ? null : $"#{v[0]:X2}{v[1]:X2}{v[2]:X2}";
    }

    /// <summary>"1a1a1a", "#1A1A1A", "#1a1" → "#1A1A1A"; null se non è un colore esadecimale.</summary>
    private static string? Normalize(string s)
    {
        s = s.TrimStart('#');
        if (Regex.IsMatch(s, "^[0-9a-fA-F]{3}$")) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        return Regex.IsMatch(s, "^[0-9a-fA-F]{6}$") ? "#" + s.ToUpperInvariant() : null;
    }


    /// <summary>Il rapporto di contrasto (WCAG) quando è sotto 3:1, cioè difficile da leggere; null se va bene o non si sa.</summary>
    private double? LowContrast
    {
        get
        {
            if (Lib.InkColor is not { } a || Lib.PaperColor is not { } b) return null;
            double la = Luminance(a), lb = Luminance(b);
            double ratio = (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
            return ratio < 3 ? ratio : null;
        }
    }

    private static double Luminance(string hex)
    {
        double Ch(int i)
        {
            double v = int.Parse(hex.Substring(1 + i * 2, 2), NumberStyles.HexNumber) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(0) + 0.7152 * Ch(1) + 0.0722 * Ch(2);
    }
}
