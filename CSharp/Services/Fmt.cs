using System.Globalization;
using System.Text.RegularExpressions;

namespace Bfontb.Services;

/// <summary>Piccoli aiuti di formattazione e per i collegamenti.</summary>
public static partial class Fmt
{
    /// <summary>Numero all'inglese, come il resto dell'interfaccia: 25,120</summary>
    public static string N(long n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    public static string Kb(long bytes) => N((long)Math.Round(bytes / 1024.0)) + " KB";

    /// <summary>Primo indirizzo web contenuto in un testo, con https:// se manca.</summary>
    public static string? UrlOf(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = UrlRe().Match(s);
        if (!m.Success) return null;
        return m.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? m.Value : "https://" + m.Value;
    }

    public static string Plural(int n, string one, string many) => n == 1 ? one : many;

    /// <summary>Id sicuro per un elemento HTML.</summary>
    public static string Id(string prefix, string s) => prefix + NonAlnumRe().Replace(s, "_");

    [GeneratedRegex(@"(https?://[^\s,;]+|www\.[^\s,;]+)", RegexOptions.IgnoreCase)] private static partial Regex UrlRe();
    [GeneratedRegex("[^a-zA-Z0-9]")] private static partial Regex NonAlnumRe();
}
