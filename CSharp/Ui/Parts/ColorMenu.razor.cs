using System.Globalization;
using System.Text.RegularExpressions;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Bfontb.Ui.Parts;

/// <summary>
/// La tendina dei colori in alto: colore del testo e dello sfondo dei testi di prova (non dell'interfaccia).
/// I colori li tiene FontLibrary (InkColor, PaperColor); App li passa al CSS come --spec-ink e --spec-paper.
/// </summary>
public partial class ColorMenu
{
    [Inject] private FontLibrary Lib { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }
    [Parameter] public string? LabelledBy { get; set; }

    private sealed record Preset(string Name, string? Ink, string? Paper);

    // la prima segue il tema; le altre valgono uguali in chiaro e in scuro
    private static readonly Preset[] Presets =
    [
        new("Ink on paper", null, null),
        new("White on black", "#FFFFFF", "#111111"),
        new("Black on yellow", "#111111", "#FFE14D"),
        new("Blue on white", "#2A3FD6", "#FFFFFF"),
        new("Red on cream", "#C8312B", "#F5EEDC"),
        new("Green on black", "#7CFF6B", "#0B0B0B"),
    ];

    private static readonly string[] Palette =
    [
        "#111111", "#3B3B3B", "#8A8A8A", "#FFFFFF", "#F5EEDC", "#FFE14D", "#FF8A5C", "#C8312B",
        "#E85D9E", "#7B4CC2", "#2A3FD6", "#3AA1D8", "#0B1F3A", "#2E8B57", "#7CFF6B", "#6B4E2E",
    ];

    private bool open;

    private void Toggle() => open = !open;
    private void Close() => open = false;
    private void OnKey(KeyboardEventArgs e) { if (e.Key == "Escape") open = false; }

    private string CurrentName => Presets.FirstOrDefault(IsCurrent)?.Name ?? "Custom";

    private bool IsCurrent(Preset p) =>
        string.Equals(p.Ink, Lib.InkColor, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Paper, Lib.PaperColor, StringComparison.OrdinalIgnoreCase);

    private static string Swatch(string? ink, string? paper) => $"color:{ink ?? "var(--ink)"};background:{paper ?? "var(--sheet)"}";

    private Task Use(string? ink, string? paper) => Lib.SetColorsAsync(ink, paper);

    private Task Pick(bool isInk, string c) => isInk ? Lib.SetColorsAsync(c, Lib.PaperColor) : Lib.SetColorsAsync(Lib.InkColor, c);

    private Task OnHex(bool isInk, ChangeEventArgs e)
    {
        var raw = (e.Value?.ToString() ?? "").Trim();
        string? c = raw.Length == 0 ? null : Normalize(raw);
        if (raw.Length > 0 && c is null) return Task.CompletedTask;   // codice non valido: si lascia com'era
        return isInk ? Lib.SetColorsAsync(c, Lib.PaperColor) : Lib.SetColorsAsync(Lib.InkColor, c);
    }

    /// <summary>"1a1a1a", "#1A1A1A", "#1a1" → "#1A1A1A"; null se non è un colore esadecimale.</summary>
    private static string? Normalize(string s)
    {
        s = s.TrimStart('#');
        if (Regex.IsMatch(s, "^[0-9a-fA-F]{3}$")) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        return Regex.IsMatch(s, "^[0-9a-fA-F]{6}$") ? "#" + s.ToUpperInvariant() : null;
    }

    // scambio: con i colori del tema (non ancora scelti) si parte da inchiostro su bianco
    private Task Swap() => Lib.SetColorsAsync(Lib.PaperColor ?? "#FFFFFF", Lib.InkColor ?? "#16171A");

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
