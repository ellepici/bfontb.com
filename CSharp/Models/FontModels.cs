namespace Bfontb.Models;

/// <summary>Un file della cartella scelta. L'Id indica il File tenuto in memoria dal browser.</summary>
public sealed record FontFile(int Id, string Path, long Size)
{
    public string FileName => Path[(Path.LastIndexOf('/') + 1)..];
    public bool IsWeb => FileName.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                         || FileName.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase);
    public bool IsDesktop => FileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                             || FileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                             || FileName.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase);
    public string[] Segments => Path.Split('/');
}

public enum LoadStatus { Idle, Loading, Ready, Error }

/// <summary>Uno stile da mostrare (un file, oppure un carattere di esempio di Google Fonts).</summary>
public sealed class FontStyle
{
    private static int _seq;

    public int Id { get; } = Interlocked.Increment(ref _seq);
    public FontFile? File { get; init; }
    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";

    /// <summary>Il formato del file in maiuscolo, dall'estensione: "OTF", "TTF", "WOFF2"… (vuoto se manca).</summary>
    public string Format
    {
        get
        {
            int dot = FileName.LastIndexOf('.');
            return dot > 0 && dot < FileName.Length - 1 ? FileName[(dot + 1)..].ToUpperInvariant() : "";
        }
    }
    public string Label { get; set; } = "";
    public int Weight { get; set; } = 400;
    public bool Italic { get; set; }
    public FontInfo? Info { get; set; }

    /// <summary>Font variabile dritto che ha il corsivo dentro di sé (asse slnt o ital): con Italic acceso vale come corsivo.</summary>
    public bool SlantsToItalic => !Italic && Info is { SlantAxis: not null, SlantValue: not null };

    /// <summary>Nelle tendine del catalogo: la scelta "corsivo" di un variabile che lo ha dentro è il suo percorso più questo segno.</summary>
    public const string ItalicMark = "#italic";

    /// <summary>Dritto o corsivo, contando anche il corsivo dentro un variabile.</summary>
    public bool HasItalic => Italic || SlantsToItalic;

    /// <summary>L'impostazione che rende corsivo un variabile, es. "slnt" -14; null se non serve.</summary>
    public string? ItalicVariation => SlantsToItalic
        ? $"\"{Info!.SlantAxis}\" {Info.SlantValue!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        : null;
    public LoadStatus Status { get; set; } = LoadStatus.Idle;
    public string? FaceName { get; set; }
    public bool IsWeb { get; init; }

    /// <summary>Solo per i caratteri di esempio: famiglia CSS già disponibile.</summary>
    public string? DemoCssFamily { get; init; }

    public bool IsDemo => DemoCssFamily is not null;

    // Caratteristiche (sans, serif, mono…): ricalcolate solo quando cambiano i dati letti dal file.
    private FontInfo? _traitsInfo;
    private Services.FontTrait? _traits;

    public Services.FontTrait Traits(string family, string variant)
    {
        if (_traits is null || !ReferenceEquals(_traitsInfo, Info))
        {
            _traits = Services.FontClassifier.Classify(this, family, variant);
            _traitsInfo = Info;
        }
        return _traits.Value;
    }
    public long Size => File?.Size ?? 0;

    /// <summary>Le proprietà CSS per disegnare questo stile.</summary>
    public string Css
    {
        get
        {
            if (IsDemo) return $"font-family:{DemoCssFamily};font-weight:{Weight};font-style:{(Italic ? "italic" : "normal")}";
            if (Status == LoadStatus.Ready) return $"font-family:\"{FaceName}\", \"Adobe NotDef\", sans-serif;font-weight:normal;font-style:normal";
            return "";
        }
    }
}

/// <summary>Famiglia madre con le sue varianti (larghezze, Mono, Text, Display, Web…).</summary>
public sealed class TypeFamily(string name)
{
    public string Name { get; } = name;
    public SortedDictionary<string, List<FontStyle>> Variants { get; } = new(StringComparer.InvariantCultureIgnoreCase);

    public void Add(string variant, FontStyle style)
    {
        if (!Variants.TryGetValue(variant, out var list)) Variants[variant] = list = [];
        list.Add(style);
    }

    public IEnumerable<FontStyle> Styles(bool includeWeb) =>
        Variants.Where(v => includeWeb || v.Key != "Web").SelectMany(v => v.Value);

    public int Count(bool includeWeb) => Styles(includeWeb).Count();

    /// <summary>Caratteristiche della famiglia: l'unione di quelle dei suoi stili (i font web non contano).</summary>
    public Services.FontTrait Traits()
    {
        var t = Services.FontTrait.None;
        foreach (var (variant, list) in Variants)
        {
            if (variant == "Web") continue;
            foreach (var s in list) t |= s.Traits(Name, variant);
        }
        return t;
    }

    public IEnumerable<string> VariantNames(bool includeWeb) =>
        Variants.Keys.Where(v => includeWeb || v != "Web")
            .OrderBy(v => v == Name ? 0 : 1).ThenBy(v => v, StringComparer.InvariantCultureIgnoreCase);

    /// <summary>Lo stile che rappresenta la famiglia nel catalogo: il regular dritto della variante base.</summary>
    public FontStyle? Representative()
    {
        var names = Variants.Keys.Where(v => v != "Web").ToList();
        var baseName = names.Contains(Name) ? Name : names.OrderBy(n => n.Length).FirstOrDefault();
        var list = baseName is not null ? Variants[baseName] : Variants.GetValueOrDefault("Web");
        if (list is null || list.Count == 0) list = Styles(true).ToList();
        return list.OrderBy(s => s.Italic).ThenBy(s => Math.Abs(s.Weight - 400)).ThenBy(s => s.FileName.Length).FirstOrDefault();
    }

    /// <summary>Stili (esclusi i web) che hanno tutte le caratteristiche richieste.</summary>
    public IEnumerable<(string Variant, FontStyle Style)> Matching(Services.FontTrait filters) =>
        Variants.Where(v => v.Key != "Web")
            .SelectMany(v => v.Value.Select(s => (v.Key, s)))
            .Where(x => (x.s.Traits(Name, x.Key) & filters) == filters);

    /// <summary>
    /// Lo stile da mostrare nel catalogo quando ci sono filtri: tra quelli che rispondono al maggior numero
    /// di filtri, il regular dritto. Senza filtri è il solito rappresentante della famiglia.
    /// </summary>
    public FontStyle? Representative(Services.FontTrait filters)
    {
        if (filters == Services.FontTrait.None) return Representative();
        static int Bits(Services.FontTrait t) => System.Numerics.BitOperations.PopCount((uint)t);
        var scored = Variants.Where(v => v.Key != "Web")
            .SelectMany(v => v.Value.Select(s => (Style: s, Score: Bits(s.Traits(Name, v.Key) & filters))))
            .ToList();
        if (scored.Count == 0) return Representative();
        int best = scored.Max(x => x.Score);
        if (best == 0) return Representative();
        return scored.Where(x => x.Score == best).Select(x => x.Style)
            .OrderBy(s => VariantRank(VariantOf(s))).ThenBy(s => s.Italic).ThenBy(s => Math.Abs(s.Weight - 400)).ThenBy(s => s.FileName.Length).First();
    }

    private string VariantOf(FontStyle s) => Variants.First(kv => kv.Value.Contains(s)).Key;

    // le varianti per altri alfabeti (Hebrew, Arabic, Cyrillic, Greek…): nel catalogo la famiglia si mostra con quella principale
    private static readonly System.Text.RegularExpressions.Regex ScriptWords = new(
        @"\b(Arabic|Hebrew|Cyrillic|CYR|Greek|GRK|Thai|Devanagari|Hindi|Bengali|Tamil|Armenian|Georgian|Japanese|JP|Korean|KR|Chinese|SC|TC|Hangul|Khmer|Lao|Ethiopic)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>0 la variante con il nome della famiglia, 1 le altre, 2 quelle per un altro alfabeto.</summary>
    private int VariantRank(string variant) =>
        variant == Name ? 0 : ScriptWords.IsMatch(variant[Math.Min(Name.Length, variant.Length)..]) ? 2 : 1;
}

/// <summary>Le correzioni dell'utente alla proposta dell'analisi.</summary>
public sealed class Corrections
{
    public Dictionary<string, string> Rename { get; set; } = [];
    public Dictionary<string, string> Merge { get; set; } = [];
    public Dictionary<string, string> Move { get; set; } = [];
    public List<string> Exclude { get; set; } = [];

    public int Count => Rename.Count + Merge.Count + Move.Count;

    public bool IsExcluded(string path) =>
        Exclude.Any(x => path == x || path.StartsWith(x + "/", StringComparison.Ordinal));
}

/// <summary>Dove finisce un file secondo la proposta.</summary>
public sealed record Placement(string Madre, string Variant, bool Duplicate, string AutoMadre, string AutoVariant, bool Guessed);

public sealed class Proposal
{
    public Dictionary<string, Placement> ByPath { get; } = new(StringComparer.Ordinal);
    public List<(string Kept, string Duplicate)> Duplicates { get; } = [];
    public List<string> Guessed { get; } = [];
    public List<string> Unreadable { get; } = [];
}

public enum OrgMode { Folders, Analysis }

/// <summary>Le pagine dell'app. Settings contiene aspetto e preparazione della cartella (prima "Prep": stessa posizione, così la vista salvata resta valida).</summary>
public enum View { Catalog, Family, Settings, Style, Credits, Feedback }

/// <summary>La pagina Feedback: segnalare un problema o proporre un'idea.</summary>
public enum FeedbackKind { Bug, Feature }
