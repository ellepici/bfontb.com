using System.Text.RegularExpressions;
using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// I nomi degli stili nelle tendine (catalogo e pagina dello stile): gli stessi ovunque, e mai due uguali.
/// </summary>
public static partial class StyleLabels
{
    // parti dei nomi dei file che non distinguono niente: "VF", "Var", "Trial", "Web"…
    private static readonly HashSet<string> NoiseWords = new(StringComparer.OrdinalIgnoreCase) { "VF", "Var", "Trial", "Web", "Font", "Fonts", "Demo", "TRIAL" };

    // dove si divide il nome di un file in parole: spazi, trattini, trattini bassi e il passaggio da minuscola a maiuscola
    [GeneratedRegex(@"[\s_\-]+|(?<=[a-z])(?=[A-Z])")]
    private static partial Regex WordRe();

    /// <summary>Il nome di uno stile nella tendina e, se serve a distinguerlo, il formato del file (scritto in piccolo, a destra).</summary>
    public readonly record struct Name(string Text, string? Format);

    /// <summary>
    /// Se due stili si chiamano uguale (es. due file "Variable", Camera e Camera Plain), davanti al nome si mette quello che
    /// distingue i loro file ("Plain Variable"). Se i file si chiamano uguale e cambia solo il formato (Americana-Bold.otf e
    /// Americana-Bold.ttf), il nome resta "Bold" e accanto, in piccolo, c'è il formato: OTF, TTF.
    /// </summary>
    public static Dictionary<FontStyle, Name> Distinct(IReadOnlyCollection<FontStyle> styles)
    {
        var names = styles.ToDictionary(s => s, s => s.Label);
        foreach (var same in styles.GroupBy(s => s.Label).Where(g => g.Count() > 1))
        {
            // si confrontano parole intere, non lettere ("ArizonaSans" e "ArizonaSerif" hanno in comune "Arizona", non "ArizonaS")
            var words = same.Select(s => WordRe().Split(Path.GetFileNameWithoutExtension(s.FileName)).Where(w => w.Length > 0).ToArray()).ToList();
            int pre = 0, suf = 0, min = words.Min(x => x.Length);
            while (pre < min && words.All(x => string.Equals(x[pre], words[0][pre], StringComparison.OrdinalIgnoreCase))) pre++;
            while (suf < min - pre && words.All(x => string.Equals(x[^(suf + 1)], words[0][^(suf + 1)], StringComparison.OrdinalIgnoreCase))) suf++;
            foreach (var (s, w) in same.Zip(words))
            {
                // le parole che restano ("Normal Variable VF"), senza quelle già nel nome dello stile e le sigle di servizio
                var part = string.Join(" ", w[pre..^suf].Where(x => !NoiseWords.Contains(x)
                    && !s.Label.Split(' ').Contains(x, StringComparer.OrdinalIgnoreCase)));
                if (part.Length > 0) names[s] = part + " " + s.Label;
            }
        }
        // ancora uguali: lo stesso stile in due formati
        var result = names.ToDictionary(kv => kv.Key, kv => new Name(kv.Value, null));
        foreach (var same in styles.GroupBy(s => names[s]).Where(g => g.Count() > 1))
        {
            var formats = same.Select(Format).ToList();
            if (formats.Distinct(StringComparer.Ordinal).Count() < 2) continue;
            foreach (var (s, f) in same.Zip(formats)) result[s] = new Name(names[s], f);
        }
        return result;
    }

    /// <summary>Il formato del file, in maiuscolo: OTF, TTF, WOFF2…</summary>
    public static string Format(FontStyle s) => Path.GetExtension(s.FileName).TrimStart('.').ToUpperInvariant();
}
