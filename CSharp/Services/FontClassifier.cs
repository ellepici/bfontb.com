using System.Text.RegularExpressions;
using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>Caratteristiche per cui si può filtrare il catalogo.</summary>
[Flags]
public enum FontTrait
{
    None = 0,
    Sans = 1,
    Serif = 2,
    Slab = 4,
    Mono = 8,
    Script = 16,
    Display = 32,
    Symbol = 64,
    Variable = 128,
}

/// <summary>
/// Classifica uno stile combinando i dati dichiarati nel file (post, PANOSE, classe IBM, fvar)
/// con le parole del nome. Il nome vince sul resto: "Sans" nel nome conta più di un PANOSE sbagliato.
/// </summary>
public static partial class FontClassifier
{
    public static readonly (FontTrait Trait, string Label)[] All =
    [
        (FontTrait.Sans, "Sans serif"),
        (FontTrait.Serif, "Serif"),
        (FontTrait.Slab, "Slab"),
        (FontTrait.Mono, "Mono"),
        (FontTrait.Script, "Script"),
        (FontTrait.Display, "Display"),
        (FontTrait.Symbol, "Symbols"),
        (FontTrait.Variable, "Variable"),
    ];

    public static string LabelOf(FontTrait t) => All.FirstOrDefault(a => a.Trait == t).Label ?? t.ToString();

    public static FontTrait Classify(FontStyle st, string family, string variant)
    {
        var info = st.Info;
        var text = $" {family} {variant} {info?.Family} {info?.Sub} {Stem(st.FileName)} ";
        text = " " + StyleNames.Spaced(text) + " ";

        // 1. parole del nome
        var byName = FontTrait.None;
        if (MonoRe().IsMatch(text)) byName |= FontTrait.Mono;
        if (SlabRe().IsMatch(text)) byName |= FontTrait.Slab;
        if (SansRe().IsMatch(text) || SansClassicRe().IsMatch(text)) byName |= FontTrait.Sans;
        else if (SerifRe().IsMatch(text) || SerifClassicRe().IsMatch(text)) byName |= FontTrait.Serif;
        if (ScriptRe().IsMatch(text)) byName |= FontTrait.Script;
        if (DisplayRe().IsMatch(text)) byName |= FontTrait.Display;
        if (SymbolRe().IsMatch(text)) byName |= FontTrait.Symbol;
        if (VariableRe().IsMatch(text)) byName |= FontTrait.Variable;

        var traits = byName;
        if (info is null) return traits;

        // 2. dati dichiarati nel file
        if (info.FixedPitch || info.PanoseProportion == 9) traits |= FontTrait.Mono;
        if (info.IsVariable) traits |= FontTrait.Variable;

        var declared = FontTrait.None;
        switch (info.PanoseFamily)
        {
            case 3: declared |= FontTrait.Script; break;
            case 4: declared |= FontTrait.Display; break;
            case 5: declared |= FontTrait.Symbol; break;
            case 2:
                if (info.PanoseSerif is >= 11 and <= 15) declared |= FontTrait.Sans;
                else if (info.PanoseSerif == 6) declared |= FontTrait.Slab;
                else if (info.PanoseSerif is >= 2 and <= 10) declared |= FontTrait.Serif;
                break;
        }
        if (declared == FontTrait.None)
        {
            declared = info.FamilyClass switch
            {
                1 or 2 or 3 or 4 or 7 => FontTrait.Serif,
                5 => FontTrait.Slab,
                8 => FontTrait.Sans,
                9 => FontTrait.Display,
                10 => FontTrait.Script,
                12 => FontTrait.Symbol,
                _ => FontTrait.None,
            };
        }

        // Se il nome dice già sans/serif/slab, la classificazione del file non lo contraddice.
        const FontTrait shape = FontTrait.Sans | FontTrait.Serif | FontTrait.Slab;
        if ((byName & shape) != 0) declared &= ~shape;
        traits |= declared;

        // 3. se nessuno lo dice, decide la forma misurata
        if ((traits & shape) == 0 && info.ShapeSerif is bool serif)
            traits |= serif ? FontTrait.Serif : FontTrait.Sans;
        return traits;
    }

    private static string Stem(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[..dot] : fileName;
    }

    [GeneratedRegex(@"\b(mono|monospace|monospaced|code|typewriter|pitch)\b")] private static partial Regex MonoRe();
    [GeneratedRegex(@"\bslab\b|\b(clarendon|rockwell|memphis|serifa|egyptienne|sentinel|lubalin|caecilia|glypha)\b")] private static partial Regex SlabRe();
    [GeneratedRegex(@"\b(sans|grotesk|grotesque|grotesken|grot|gothic|antique olive|neo grotesk)\b")] private static partial Regex SansRe();
    [GeneratedRegex(@"\b(helvetica|univers|futura|akzidenz|frutiger|avenir|gill|optima|din|eurostile|franklin|avant garde|myriad|vectora|syntax|trade gothic|news gothic|kabel|maison|graphik|suisse int|neue haas|unica|apercu|circular|gt america|gt walsheim|gt flexa|favorit|diatype|monument|aktiv|inter|roboto|poppins|manrope|lato|montserrat)\b")] private static partial Regex SansClassicRe();
    [GeneratedRegex(@"(?<!sans )\bserif\b|\bantiqua\b")] private static partial Regex SerifRe();
    [GeneratedRegex(@"\b(garamond|caslon|bodoni|didot|baskerville|times|jenson|bembo|plantin|sabon|minion|janson|palatino|century|cheltenham|goudy|perpetua|joanna|electra|caledonia|fournier|granjon|galliard|ehrhardt|bulmer|walbaum|tiempos|lyon|freight|publico|canela|saol|gt sectra|gt super|gt alpina|reckless|newsreader|fraunces|lora|merriweather|spectral|crimson|cormorant|source serif)\b")] private static partial Regex SerifClassicRe();
    [GeneratedRegex(@"\b(script|hand|handwritten|brush|calligraph\w*|cursive|marker|signature|chancery)\b")] private static partial Regex ScriptRe();
    [GeneratedRegex(@"\b(stencil|inline|outline|shadow\w*|decorative|ornamental|fraktur|blackletter|pixel|bitmap|graffiti|poster|fat ?face)\b")] private static partial Regex DisplayRe();
    [GeneratedRegex(@"\b(dingbats?|symbols?|pi|ornaments?|emoji|icons?|awesome|math)\b")] private static partial Regex SymbolRe();
    [GeneratedRegex(@"\b(variable|vf|var)\b")] private static partial Regex VariableRe();
}
