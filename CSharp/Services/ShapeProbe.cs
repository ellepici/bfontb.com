using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Capisce se un carattere ha le grazie guardando la forma di "I" e "l":
/// nei serif la testa e il piede dell'asta sono molto più larghi dell'asta stessa, nei sans no.
/// Il browser disegna i glifi (bfontb.js → probeShape); qui si misura e si decide.
/// </summary>
public static class ShapeProbe
{
    public const string Glyphs = "Il";
    public const int Size = 200;

    /// <summary>Si misura solo lo stile dritto e regolare di un font desktop, quando nulla dice già la sua forma.</summary>
    public static bool ShouldProbe(FontFile f, FontInfo info)
    {
        if (!f.IsDesktop || info.Error || info.Note is not null) return false;
        if (info.FixedPitch || info.PanoseProportion == 9) return false;                    // i mono hanno spesso grazie anche se "sans"
        if (info.PanoseFamily is 3 or 4 or 5) return false;                                 // script, decorativi, simboli
        if (info.PanoseFamily == 2 && info.PanoseSerif is >= 2 and <= 15) return false;    // la forma è già dichiarata
        if (info.FamilyClass is >= 1 and <= 8) return false;
        if (info.IsItalicFlag || StyleNames.IsItalic(info.Sub ?? "")) return false;
        int w = info.WeightClass ?? StyleNames.WeightOf(info.Sub ?? "");
        return w is >= 300 and <= 600;
    }

    /// <summary>true = con grazie, false = senza grazie, null = non si capisce (glifo mancante, forma ambigua).</summary>
    public static bool? Decide(int[] rows)
    {
        if (rows.Length != Glyphs.Length * Size) return null;
        var i = Measure(rows.AsSpan(0, Size));
        var l = Measure(rows.AsSpan(Size, Size));
        if (i is null || l is null) return null;

        // "I": nei serif si allargano sia la testa sia il piede.
        double iBoth = Math.Min(i.Value.Top, i.Value.Bottom);
        double iMax = Math.Max(i.Value.Top, i.Value.Bottom);
        double lMax = Math.Max(l.Value.Top, l.Value.Bottom);

        if (iBoth >= 1.45 && lMax >= 1.35) return true;
        if (iMax <= 1.25 && lMax <= 1.3) return false;
        return null;
    }

    /// <summary>Rapporto tra la larghezza massima in testa/piede e la larghezza dell'asta.</summary>
    private static (double Top, double Bottom)? Measure(ReadOnlySpan<int> widths)
    {
        int first = -1, last = -1;
        for (int y = 0; y < widths.Length; y++)
            if (widths[y] > 0) { if (first < 0) first = y; last = y; }
        if (first < 0) return null;
        int h = last - first + 1;
        if (h < 40) return null;   // glifo assente o troppo piccolo

        var middle = new List<int>();
        for (int y = first + h / 4; y <= last - h / 4; y++) middle.Add(widths[y]);
        middle.Sort();
        double stem = middle[middle.Count / 2];
        if (stem <= 0) return null;

        int band = Math.Max(2, h * 12 / 100);
        int top = 0, bottom = 0;
        for (int y = first; y < first + band; y++) top = Math.Max(top, widths[y]);
        for (int y = last - band + 1; y <= last; y++) bottom = Math.Max(bottom, widths[y]);
        return (top / stem, bottom / stem);
    }
}
