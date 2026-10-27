using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>Sei caratteri open source (da Google Fonts) mostrati prima di aprire una cartella.</summary>
public static class DemoFonts
{
    private static readonly (string Family, string Variant, string Foundry, (int W, bool It, string Label)[] Styles)[] Data =
    [
        ("Fraunces", "Fraunces", "Undercase Type", [(300, false, "Light"), (400, false, "Regular"), (400, true, "Italic"), (600, false, "SemiBold"), (900, false, "Black")]),
        ("Newsreader", "Newsreader", "Production Type", [(400, false, "Regular"), (400, true, "Italic"), (600, false, "SemiBold")]),
        ("Instrument Serif", "Instrument Serif", "Instrument", [(400, false, "Regular"), (400, true, "Italic")]),
        ("Bricolage Grotesque", "Bricolage Grotesque", "Mathieu Triay", [(400, false, "Regular"), (700, false, "Bold")]),
        ("DM", "DM Mono", "Colophon Foundry", [(300, false, "Light"), (400, false, "Regular"), (500, false, "Medium")]),
        ("Syne", "Syne", "Bonjour Monde", [(400, false, "Regular"), (600, false, "SemiBold"), (800, false, "ExtraBold")]),
    ];

    public static Dictionary<string, TypeFamily> Create()
    {
        var families = new Dictionary<string, TypeFamily>();
        foreach (var (fam, variant, foundry, styles) in Data)
        {
            var tf = new TypeFamily(fam);
            foreach (var (w, it, label) in styles)
            {
                var file = $"{variant.Replace(" ", "")}-{label}.otf";
                tf.Add(variant, new FontStyle
                {
                    Path = $"Sample/{fam}/{file}", FileName = file, Label = label, Weight = w, Italic = it,
                    DemoCssFamily = $"\"{variant}\", serif", Status = LoadStatus.Ready,
                    Info = new FontInfo { Full = $"{variant} {label}", Foundry = foundry, License = "SIL Open Font License 1.1", LicenseUrl = "https://openfontlicense.org" },
                });
            }
            families[fam] = tf;
        }
        return families;
    }
}
