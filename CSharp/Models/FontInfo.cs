namespace Bfontb.Models;

/// <summary>
/// Dati letti dalle tabelle OpenType di un file (name, OS/2, maxp).
/// Sono i crediti scritti dalla fonderia: designer, copyright, licenza, siti.
/// </summary>
public sealed class FontInfo
{
    public string? Copyright { get; set; }
    public string? Family { get; set; }
    public string? Sub { get; set; }
    public string? Full { get; set; }
    public string? Version { get; set; }
    public string? Trademark { get; set; }
    public string? Foundry { get; set; }
    public string? FoundryGuess { get; set; }
    public string? Designer { get; set; }
    public string? Description { get; set; }
    public string? VendorUrl { get; set; }
    public string? DesignerUrl { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public string? Year { get; set; }
    public int? WeightClass { get; set; }
    public int? WidthClass { get; set; }
    public int? Glyphs { get; set; }
    public string? Vendor { get; set; }
    public int? FsSelection { get; set; }

    // ---- classificazione dichiarata dal file ----
    /// <summary>Classe IBM della famiglia (OS/2 sFamilyClass, byte alto): 1-7 serif, 8 sans, 9 decorativo, 10 script, 12 simboli.</summary>
    public int? FamilyClass { get; set; }
    /// <summary>PANOSE: tipo di famiglia (2 testo, 3 scritto a mano, 4 decorativo, 5 simboli).</summary>
    public int? PanoseFamily { get; set; }
    /// <summary>PANOSE: stile delle grazie (2-10 con grazie, 11-15 senza).</summary>
    public int? PanoseSerif { get; set; }
    /// <summary>PANOSE: proporzioni (9 = monospaziato).</summary>
    public int? PanoseProportion { get; set; }
    /// <summary>Tabella post: il font dichiara di essere a spaziatura fissa.</summary>
    public bool FixedPitch { get; set; }
    /// <summary>Il file contiene assi di variazione (tabella fvar).</summary>
    public bool IsVariable { get; set; }
    /// <summary>Misura della forma (ShapeProbe): true = con grazie, false = senza, null = non misurato o incerto.</summary>
    public bool? ShapeSerif { get; set; }

    /// <summary>Versione dei dati letti: le analisi più vecchie vanno rifatte per avere la classificazione.</summary>
    public int Schema { get; set; }
    public const int CurrentSchema = 3;

    /// <summary>Messaggio quando il file è leggibile ma non contiene crediti (es. woff2).</summary>
    public string? Note { get; set; }

    /// <summary>Il file non si apre (danneggiato o formato non supportato).</summary>
    public bool Error { get; set; }

    /// <summary>Copia parziale estratta da un PDF (o salvata da un sito): ha solo i caratteri usati lì.
    /// Si riconosce dal prefisso di sei maiuscole e un più nel nome, "ZOUTJY+Rubis-Black".</summary>
    public bool Subset { get; set; }

    /// <summary>Versione ridotta salvata nella cache dell'analisi (testi lunghi tagliati).</summary>
    public bool Slim { get; set; }

    public bool IsItalicFlag => FsSelection is int f && (f & 1) == 1;

    public FontInfo ToSlim()
    {
        static string? Cut(string? s, int n) => s is { Length: > 0 } && s.Length > n ? s[..n] + "…" : s;
        return new FontInfo
        {
            Family = Family, Sub = Sub, Full = Full, Version = Version, Foundry = Foundry, FoundryGuess = FoundryGuess,
            Designer = Designer, VendorUrl = VendorUrl, DesignerUrl = DesignerUrl, LicenseUrl = LicenseUrl, Year = Year,
            WeightClass = WeightClass, WidthClass = WidthClass, Glyphs = Glyphs, Vendor = Vendor, FsSelection = FsSelection,
            Note = Note, Error = Error, Slim = true, Subset = Subset,
            FamilyClass = FamilyClass, PanoseFamily = PanoseFamily, PanoseSerif = PanoseSerif, PanoseProportion = PanoseProportion,
            FixedPitch = FixedPitch, IsVariable = IsVariable, ShapeSerif = ShapeSerif, Schema = Schema,
            Copyright = Cut(Copyright, 400), Trademark = Cut(Trademark, 300), License = Cut(License, 400),
        };
    }

    public static readonly IReadOnlyDictionary<int, string> WidthNames = new Dictionary<int, string>
    {
        [1] = "Ultra Condensed", [2] = "Extra Condensed", [3] = "Condensed", [4] = "Semi Condensed", [5] = "Normal",
        [6] = "Semi Expanded", [7] = "Expanded", [8] = "Extra Expanded", [9] = "Ultra Expanded",
    };

    /// <summary>Riga breve per il catalogo: "di Designer · Fonderia · Anno".</summary>
    public string CreditLine()
    {
        var foundry = Foundry ?? FoundryGuess;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Designer)) parts.Add("by " + Designer);
        if (!string.IsNullOrWhiteSpace(foundry) && foundry != Designer) parts.Add(foundry!);
        if (!string.IsNullOrWhiteSpace(Year)) parts.Add(Year!);
        return string.Join(" · ", parts);
    }
}
