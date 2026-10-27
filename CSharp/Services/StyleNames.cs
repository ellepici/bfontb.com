using System.Text.RegularExpressions;

namespace Bfontb.Services;

/// <summary>
/// Regole per capire peso, corsivo, nome dello stile e famiglia a partire dai nomi
/// (nome del file o nomi scritti dentro il font).
/// </summary>
public static partial class StyleNames
{
    /// <summary>"ExtraBoldItalic" → "extra bold italic".</summary>
    public static string Spaced(string s) =>
        GluedSlantRe().Replace(CamelRe().Replace(s, "$1 $2").Replace('-', ' ').Replace('_', ' ').ToLowerInvariant(), "$1 $2");

    // nomi tutti minuscoli e attaccati ("texgyre-bolditalic"): il corsivo si stacca dal peso ("bold italic")
    [GeneratedRegex(@"([a-z])(italic|oblique|inclined)")]
    private static partial Regex GluedSlantRe();

    // anche i nomi tedeschi degli stili (Söhne, Walter Neue, i caratteri Berthold): Mager, Leicht, Buch, Halbfett, Fett…
    private static readonly (Regex Re, int Weight)[] WeightRules =
    [
        (new(@"\bextra ?leicht\b|\bextraleicht\b"), 200),
        (new(@"\bdreiviertel ?fett\b"), 700),
        (new(@"\bhalb ?fett\b"), 600),
        (new(@"\b(extra|ultra) ?fett\b"), 900),
        (new(@"\bfett\b"), 700),
        (new(@"\bkr(ä|ae|a)ftig\b|\bmittel\b"), 500),
        (new(@"\bleicht\b|\bmager\b"), 300),
        (new(@"\bbuch\b"), 400),
        (new(@"hair ?line|\bhair\b"), 100),
        (new(@"\bthin\b|\bth\b"), 100),
        (new(@"(extra|ultra) ?light|xlight|\bxlt\b|ult ?lt"), 200),
        (new(@"(semi|demi) ?light"), 350),
        (new(@"\blight\b|\blt\b|\blig\b"), 300),
        (new(@"\bbook\b|\bbk\b"), 380),
        (new(@"\bmedium\b|\bmed\b|\bmd\b"), 500),
        (new(@"(semi|demi) ?bold|\bsm ?bd\b|\bdemi\b"), 600),
        (new(@"(extra|ultra) ?bold|xbold|\bxbd\b|\bheavy\b|\bhv\b"), 800),
        (new(@"\bblack\b|\bblk\b|\bultra\b|\bfat\b|\bsuper\b|\bx ?black\b"), 900),
        (new(@"\bbold\b|\bbd\b"), 700),
    ];

    public static int WeightOf(string label)
    {
        var s = " " + Spaced(label) + " ";
        foreach (var (re, w) in WeightRules) if (re.IsMatch(s)) return w;
        return 400;
    }

    public static bool IsItalic(string label) => ItalicRe().IsMatch(" " + Spaced(label) + " ");

    /// <summary>
    /// Nome leggibile dello stile ricavato dal nome del file (quando il font non è stato analizzato):
    /// "GTAmerica-CondensedBold-Trial.otf" → "Condensed Bold", "agipo_bold_italic.otf" → "Bold Italic",
    /// "Agipo-Bold-Italic.otf" → "Bold Italic", "Agipo-Regular-Italic.otf" → "Italic".
    /// Si toglie il nome della famiglia (le parole prima della prima parola di stile) e le diciture come Trial.
    /// </summary>
    public static string StyleLabel(string fileName)
    {
        var stem = Stem(fileName);
        // i variabili di Google: "Lora-Italic-VariableFont_wght" → "Lora Italic Variable"
        var clean = VariableFontRe().Replace(TrialWordRe().Replace(stem, " "), " Variable ");
        var words = clean.Split(['-', '_', ' ', '.'], StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(w => Spaced(w).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        if (words.Count == 0) return stem;
        // lo stile è la coda del nome fatta solo di parole di stile; prima c'è la famiglia
        int first = -1;
        for (int i = words.Count - 1; i >= 0 && IsStyleWord(words, i); i--) first = i;
        // il primo pezzo del nome (prima di - o _) è la famiglia, anche se contiene parole di stile:
        // "ABCAsfaltCondensed-Light" → "Light" (Condensed è della variante, che ha la sua cartella)
        var pieces = clean.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length >= 2 && first >= 0)
        {
            int familyWords = Spaced(pieces[0]).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            first = first < familyWords ? (familyWords < words.Count ? familyWords : -1) : first;
        }
        // nessuna parola di stile riconosciuta: l'ultimo pezzo del nome, come si faceva prima ("NeueHaas-Mediu" → "Mediu")
        if (first < 0)
        {
            var parts = TrialWordRe().Replace(stem, "").Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return stem;
            first = Math.Max(0, words.Count - Spaced(parts[^1]).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        }
        var style = words.Skip(first).ToList();
        // il regular corsivo si chiama semplicemente Italic
        if (style.Count == 2 && style[0] is "regular" && IsItalic(style[1])) style.RemoveAt(0);
        var label = string.Join(' ', style.Select(Word));
        return label.Length > 0 ? label : stem;
    }

    private static readonly Regex GermanWeight = new(@"\b(extra ?leicht|leicht|mager|buch|mittel|kr(ä|ae|a)ftig|halb ?fett|dreiviertel ?fett|(extra|ultra)? ?fett)\b");

    /// <summary>
    /// Per gli stili con il nome tedesco ("Fett", "Halbfett", "Buch"): il nome inglese corrispondente al peso
    /// (dal file, se analizzato), per esempio "Bold". Null se il nome è già in inglese.
    /// </summary>
    public static string? EnglishWeight(string label, int weight)
    {
        if (!GermanWeight.IsMatch(" " + Spaced(label) + " ")) return null;
        return (int)Math.Round(Math.Clamp(weight, 100, 900) / 100.0) switch
        {
            1 => "Thin", 2 => "ExtraLight", 3 => "Light", 4 => "Regular", 5 => "Medium",
            6 => "SemiBold", 7 => "Bold", 8 => "ExtraBold", _ => "Black",
        };
    }

    /// <summary>Una parola dello stile per esteso e con la maiuscola: le abbreviazioni dei file ("It", "Bd", "Lt") diventano parole intere.</summary>
    private static string Word(string w) => w switch
    {
        "it" or "ita" or "ital" or "kursiv" => "Italic",
        "obl" => "Oblique",
        "bd" => "Bold",
        "lt" or "lig" => "Light",
        "md" or "med" => "Medium",
        "bk" => "Book",
        "blk" => "Black",
        "hv" => "Heavy",
        "th" => "Thin",
        "cn" or "cond" => "Condensed",
        _ when w.Length <= 2 => w.ToUpperInvariant(),
        _ => char.ToUpperInvariant(w[0]) + w[1..],
    };

    /// <summary>
    /// La parola in posizione i descrive lo stile (peso, corsivo, larghezza, sottofamiglia), non il nome della famiglia.
    /// Regular, Roman, Book… contano solo se dopo c'è al massimo il corsivo: in "Times New Roman Bold" Roman è della famiglia.
    /// </summary>
    private static bool IsStyleWord(List<string> words, int i)
    {
        var w = words[i];
        if (WeightOf(w) != 400 || IsItalic(w) || WidthWord.IsMatch(w) || VariantWord.IsMatch(w)) return true;
        if (w is "semi" or "demi" or "extra" or "ultra") return i + 1 < words.Count;
        if (w is "regular" or "roman" or "normal" or "plain" or "book" or "upright" or "buch")
            return words.Skip(i + 1).All(IsItalic);
        return false;
    }

    [GeneratedRegex(@"variable ?font(_[a-z,]+)*", RegexOptions.IgnoreCase)] private static partial Regex VariableFontRe();

    /// <summary>Famiglia probabile ricavata dal nome del file, quando il font non la dichiara.</summary>
    public static string GuessFamily(string fileName)
    {
        var stem = TrialWordRe().Replace(Stem(fileName), "");
        var first = stem.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? stem;
        var g = AcronymRe().Replace(CamelRe().Replace(first, "$1 $2"), "$1 $2").Trim();
        return g.Length > 0 ? g : fileName;
    }

    private static string Stem(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    // ---- regole per la proposta dell'analisi ----

    public static readonly Regex WidthWord = new(@"^(semi|extra|ultra|x{1,4})?(condensed|cond|cn|narrow|compressed|extended|expanded|wide|compact)$|^(breit|schmal)$", RegexOptions.IgnoreCase);
    public static readonly Regex VariantWord = new(@"^(mono|monospace|text|display|headline|caption|micro|big|small|fine|sans|serif|slab|rounded|stencil|poster|variable|greek|cyrillic|arabic|hebrew|inktrap|subhead|deck|titling)$", RegexOptions.IgnoreCase);
    private static readonly Regex DropWord = new(@"^(std|pro|lt|mt|bt|w0\d|w1g|ltstd|trial|test|demo|unlicensed|webfont|web)$", RegexOptions.IgnoreCase);
    private static readonly Regex Modifier = new(@"^(semi|extra|ultra)$", RegexOptions.IgnoreCase);

    public static readonly IReadOnlyDictionary<int, string> WidthClassNames = new Dictionary<int, string>
    {
        [1] = "UltraCondensed", [2] = "ExtraCondensed", [3] = "Condensed", [4] = "SemiCondensed",
        [6] = "SemiExpanded", [7] = "Expanded", [8] = "ExtraExpanded", [9] = "UltraExpanded",
    };

    /// <summary>Divide un nome in parole e toglie diciture come Std, Pro, Trial.</summary>
    public static List<string> CleanWords(string? s)
    {
        s = (s ?? "").Replace('_', ' ').Trim();
        if (!s.Contains(' ')) s = CamelRe().Replace(s, "$1 $2");   // CamelCase solo se il nome non ha spazi
        return s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !DropWord.IsMatch(w)).ToList();
    }

    /// <summary>
    /// Famiglia madre e variante per un file analizzato.
    /// Esempio: famiglia "GT America", stile "Condensed Bold" → madre "GT America", variante "GT America Condensed".
    /// </summary>
    public static (string Madre, string Variant, bool Guessed) Propose(Models.FontInfo a, string fileName)
    {
        var words = CleanWords(a.Family);
        bool guessed = false;
        if (words.Count == 0) { words = CleanWords(GuessFamily(fileName)); guessed = true; }
        if (words.Count == 0) words = [fileName];

        for (int i = 0; i < words.Count - 1; i++)
            if (Modifier.IsMatch(words[i]) && WidthWord.IsMatch(words[i + 1]))
            {
                words[i] += words[i + 1];
                words.RemoveAt(i + 1);
            }

        if (!words.Any(w => WidthWord.IsMatch(w)))
        {
            var sub = CleanWords(a.Sub);
            string? width = null;
            for (int i = 0; i < sub.Count; i++)
                if (WidthWord.IsMatch(sub[i]))
                {
                    width = (i > 0 && Modifier.IsMatch(sub[i - 1]) ? sub[i - 1] : "") + sub[i];
                    break;
                }
            if (width is null && a.WidthClass is int wc && WidthClassNames.TryGetValue(wc, out var wn)) width = wn;
            if (width is not null) words.Add(char.ToUpperInvariant(width[0]) + width[1..]);
        }

        var variant = string.Join(' ', words);
        var baseWords = words.ToList();
        while (baseWords.Count > 1 && (WidthWord.IsMatch(baseWords[^1]) || VariantWord.IsMatch(baseWords[^1])))
            baseWords.RemoveAt(baseWords.Count - 1);
        return (string.Join(' ', baseWords), variant, guessed);
    }

    [GeneratedRegex("([a-z])([A-Z])")] private static partial Regex CamelRe();
    [GeneratedRegex("([A-Z]+)([A-Z][a-z])")] private static partial Regex AcronymRe();
    [GeneratedRegex("(unlicensed|trial|test|demo|webfont)", RegexOptions.IgnoreCase)] private static partial Regex TrialWordRe();
    [GeneratedRegex(@"(italic|oblique|\bobl\b|\bit\b|\bita\b|kursiv|slant|corsiv|cilati|\bo\b)")] private static partial Regex ItalicRe();
}
