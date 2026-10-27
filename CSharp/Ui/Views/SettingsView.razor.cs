using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

/// <summary>La pagina delle impostazioni (pulsante con l'ingranaggio in alto a destra).</summary>
public partial class SettingsView
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    /// <summary>Le sezioni della pagina, per l'indice a sinistra (SettingsNav): id dell'ancora, titolo, se è una sottosezione.</summary>
    public static readonly (string Id, string Title, bool Sub)[] Sections =
    [
        ("set-guide", "Organising your font folder", false),
        ("set-steps", "Structure and steps", true),
        ("set-ai", "Let an AI do it", true),
        ("set-prep", "Folder preparation", false),
        ("prep-folder", "Your folder", true),
        ("prep-analysis", "Font analysis", true),
        ("prep-organise", "How to organise the catalog", true),
        ("prep-proposal", "Proposed families", true),
    ];


    // ---- guida: come ordinare la cartella (le regole sono quelle con cui l'app legge le cartelle, vedi FamilyOrganizer.FromFolders) ----

    private const string Tree =
        "Fonts/\n" +
        "├─ ABC Marfa/                 one folder per family\n" +
        "│  ├─ ABC Marfa/              one subfolder per variant…\n" +
        "│  │  ├─ ABCMarfa-Regular.otf\n" +
        "│  │  ├─ ABCMarfa-Bold.otf\n" +
        "│  │  └─ ABCMarfa-Bold-Italic.otf\n" +
        "│  ├─ ABC Marfa Mono/\n" +
        "│  ├─ ABC Marfa Variable/\n" +
        "│  ├─ Web/                    .woff and .woff2\n" +
        "│  └─ Extra/                  licences, PDFs, specimens (ignored)\n" +
        "├─ Bodoni/                    a single variant:\n" +
        "│  ├─ Bodoni-Roman.otf        files straight in the family folder\n" +
        "│  └─ Bodoni-Bold.otf\n" +
        "└─ _duplicates/               set aside, ignored by the app";

    private static readonly (string Title, string Text)[] Steps =
    [
        ("Work on a copy.", "Duplicate the folder or check your backup first. If it lives in iCloud, choose “Keep Downloaded” in the Finder so every file is really on your Mac."),
        ("One folder per family.", "Name it as the foundry names the typeface, in normal capitalisation: “ABC Marfa”, “Adobe Garamond”. No versions, dates, “Trial”, “OTF” or “Desktop” in the name."),
        ("Variants in subfolders.", "If a family has more variants (widths like Condensed or Extended, optical sizes like Text or Display, Mono, Variable…) give each one a subfolder with its full name, the main one too. A family with a single variant keeps its files straight in its folder."),
        ("Weights stay together.", "Thin, Regular, Bold and their italics are styles, not variants: they go in the same folder. The catalog orders them from the lightest."),
        ("Readable file names.", "Name each file Family-Weight-Italic, from the names inside the font: “Agipo-Regular.otf”, “Agipo-Bold-Italic.otf”, “ABCMarfaMono-Light.otf”. The family without spaces, the weight in one word (ExtraLight, SemiBold…), “Italic” only for the italics, “Trial” at the end for trial fonts. Apps find fonts by the names inside them, so renaming the files doesn’t break your documents."),
        ("Web fonts in “Web”.", ".woff and .woff2 files go in a “Web” subfolder of their family: the catalog hides them unless you tick “include Web”."),
        ("Everything else in “Extra”.", "Licences, PDFs, specimens and images go in an “Extra” subfolder: the catalog skips it."),
        ("Duplicates aside, never deleted.", "The same font as both .otf and .ttf, or copies like “Regular (2).otf”: keep one (preferably the .otf) and move the others into “_duplicates” at the top of the folder. The catalog ignores it, so you can delete it calmly later."),
        ("Open the folder here.", "If Folder preparation below says “Looks already sorted by family”, you’re done. If not, Font analysis reads the names inside the fonts and suggests how to group them."),
    ];

    /// <summary>
    /// Il prompt da dare a un'AI che può leggere e spostare i file (per esempio con fontTools): le stesse regole della guida,
    /// più i casi incontrati nelle cartelle vere (nomi dei PDF, refusi delle fonderie, nomi tedeschi, file nella cartella
    /// sbagliata). Chiede prima un piano da approvare, poi un registro per tornare indietro; non cancella mai niente.
    /// </summary>
    private const string Prompt = """
        You have access to my font folder. Reorganise it so that every typeface family has its own folder and every font file has a readable, uniform name, following the rules below. This is my working library: only move and rename files. Never delete anything, never change the contents of a file, never unzip archives.

        ━━ 1. BEFORE YOU START
        1.1 Work on the folder I gave you and nothing outside it. Don't follow symbolic links and skip hidden and system files (.DS_Store, Icon\r, ._*).
        1.2 If the folder is in iCloud or another cloud drive, some files may exist only as placeholders: if a file can't be read, stop and ask me to download the whole folder ("Keep Downloaded" in the Finder) instead of skipping it.
        1.3 Count every file at the start. The same number must be there at the end (moved files included).

        ━━ 2. TARGET STRUCTURE
        <font folder>/
          <Family>/                 one folder per family: "ABC Marfa", "Adobe Garamond", "Rubis"
            <font files>            a family with a single variant: its files go straight here
            <Family Variant>/       a family with several variants: one subfolder each, the main one too, with its full name:
                                    "ABC Marfa", "ABC Marfa Mono", "ABC Marfa Condensed", "ABC Marfa Variable"
            Web/                    the .woff and .woff2 files of that family
            Extra/                  licences, PDFs, specimens, images, archives and every other file that is not a font
          _duplicates/              duplicates set aside, keeping their original relative path
          _organise_log/            your plan, your log and your report

        ━━ 3. IDENTIFY EACH FONT FROM THE DATA INSIDE IT
        3.1 Read the name table of every font (fontTools or similar): typographic family (ID 16) or family (ID 1), typographic subfamily (ID 17) or subfamily (ID 2), full name (ID 4), PostScript name (ID 6), foundry and designer (IDs 8 and 9). Also read the weight (OS/2 usWeightClass), the italic flags (OS/2 fsSelection, head macStyle) and whether it is variable (fvar table). For .ttc/.otc collections read every font inside.
        3.2 Use the file and folder names only to confirm, or for fonts that can't be read.
        3.3 Clean the names before using them:
            - Fonts extracted from PDFs have six capitals and a plus in front, and the PostScript name as their family: "ZOUTJY+Rubis-BoldItalic" → family "Rubis", style "Bold Italic". Remove the prefix and split family and style.
            - Drop licensing and packaging words that aren't part of the name: Std, Pro, LT, MT, BT, Unlicensed, Trial, Demo, Test, Webfont, OTF, TTF, Desktop. (Remember Trial or Demo for the file name, rule 5.)
            - Foundries sometimes misspell their own fonts ("ABC Asflat" inside an ABC Asfalt file) or use accents inconsistently ("Landnáma"/"Landnama"): use the spelling the foundry uses on its website and in the rest of the family.
            - Translate localised style names into English: Kursiv → Italic, Fett → Bold, Halbfett → SemiBold, Buch → Book, Mager → Light, Leicht → Light, Mittel → Medium, Schmal → Condensed, Breit → Extended. When a whole family has a German name and an official English one (AG Buch BQ → AG Book), use the English one and tell me.
            - Some fonts saved from websites have meaningless names ("Mnolrwxsnkatqzpwfxfsladpaya", "Copyright Type Supply"): identify them from the file name, the folder, the foundry fields or the glyph count, and list them in the report.
        3.4 Mark as partial copies the fonts extracted from PDFs or websites (prefix like "ABCDEF+", very few glyphs): they keep their place in their family but go in the report, so I know they are incomplete.

        ━━ 4. GROUP BY FAMILY
        4.1 Group by family, not by foundry. Everything that shares the family name goes in the same family folder: "ABC Marfa Mono" and "ABC Marfa Semi Mono" belong in "ABC Marfa", "Neue Haas Grotesk Text" and "Neue Haas Grotesk Display" in "Neue Haas Grotesk".
        4.2 Variants are widths (Condensed, Compressed, Narrow, Extended, Expanded, Wide…), optical sizes (Text, Display, Caption, Subhead, Micro, Poster…), sub-families (Mono, Semi Mono, Sans, Serif, Slab, Rounded, Stencil, Inline…) and variable fonts (their own "<Family> Variable" subfolder; a variable italic goes there too).
        4.3 Weights and italics are NOT variants: Thin, ExtraLight, Light, Regular, Book, Medium, SemiBold, Bold, ExtraBold, Black, Heavy and their Italic or Oblique stay together in the same folder.
        4.4 Folder names: the family name as the foundry writes it, in normal capitalisation ("ABC Marfa", not "ABCMARFA" or "abc marfa"). Keep the foundry prefix only when it is part of the commercial name (ABC, PP, GT, TT…), never add one. No versions, dates, "Trial", "Demo", "OTF", "TTF", "Desktop", "Fonts" or "Webfonts".
        4.5 If my folder already uses a name for a family, keep my name unless it is clearly a mistake, and tell me where my name and the font's name differ.
        4.6 Fonts in the wrong place: a file whose family doesn't match its folder (for example "Syne" inside "Söhne", or "Gza" inside a folder called "Bold") moves to the folder of its own family, created if needed.
        4.7 Loose files at the top of the folder go into their family folder.
        4.8 Folders that hold several unrelated families (a pack, a downloads folder, an old export) are split into one folder per family.

        ━━ 5. FILE NAMES
        5.1 Rename every font file as <Family>-<Weight>[-Italic][-Trial].<ext>, built from the names inside the font:
            - <Family> = the full name of the variant without spaces, keeping its capitals: "ABC Marfa" → "ABCMarfa", "ABC Marfa Mono" → "ABCMarfaMono", "ABC Asfalt Condensed" → "ABCAsfaltCondensed". Letters and digits only, no accents (ä → a, é → e).
            - <Weight> = one CamelCase word: Hairline, Thin, ExtraLight, Light, Book, Regular, Medium, SemiBold, Bold, ExtraBold, Black, Heavy. Keep the font's own word when it has one (Roman, Fat, Poster, a number like 40 or 65), translated into English (rule 3.3). If the style names are missing or contradictory, use usWeightClass: 100 Thin, 200 ExtraLight, 300 Light, 400 Regular, 500 Medium, 600 SemiBold, 700 Bold, 800 ExtraBold, 900 Black.
            - "-Italic" only for italics ("-Oblique" if the font calls it so), always after the weight: "Agipo-Regular-Italic.otf", "Agipo-Bold-Italic.otf".
            - Other style words that are not the weight stay after it, in CamelCase: "AGBook-Bold-Outline.otf", "Haarlem-Regular-SmallCaps.otf".
            - Variable fonts: "<Family>-Variable[-Italic]", e.g. "ABCMarfa-Variable.ttf", "ABCMarfa-Variable-Italic.ttf".
            - "-Trial" or "-Demo" at the very end for trial and demo fonts, so they stay recognisable.
            - Keep the extension, in lower case (.otf, .ttf, .woff, .woff2, .ttc).
        5.2 Never repeat a word: no "HeavyHeavy", "FactorAFactorA", "Bold-Bold". If the family name already contains the width or the optical size, don't add it again.
        5.3 If two files would get the same name: identical fonts → duplicates (rule 6); different fonts → add what tells them apart (version, the font's own style word) or leave them as they are and ask me.
        5.4 Rename in two passes (first to temporary names, then to the final ones) so that a chain of renames never overwrites a file.
        5.5 Web fonts follow the same rule inside Web/: "ABCMarfa-Bold.woff2".

        ━━ 6. DUPLICATES
        6.1 Duplicates are: byte-identical files; copies like "Regular (2).otf" or "Regular copy.otf"; the same font in .otf and .ttf; the same font in two folders.
        6.2 Keep one in place: the .otf over the .ttf, then the most recent version (name ID 5), then the complete one over a partial copy. Move the others into _duplicates/, keeping their original relative path.
        6.3 Two files with the same name but a different version or a different number of glyphs are not duplicates: keep both, add the version to the name of the older one, and tell me.

        ━━ 7. WHAT YOU DON'T DO
        7.1 Never delete, overwrite or edit files. Never unzip archives: put them in Extra/ and list them.
        7.2 Don't guess. Files you can't place or name with confidence stay where they are, listed in the report.
        7.3 Folders that end up empty: list them, don't remove them unless I say so.

        ━━ 8. PROCESS
        8.1 Scan the whole folder first, without changing anything.
        8.2 Write the plan to _organise_log/plan.csv with the columns: current path, new path, action (move, rename, duplicate, extra, keep), reason, family, variant, style, weight, italic, version.
        8.3 Show me a summary and wait for my OK before moving or renaming anything:
            - how many files, families and variants, before and after;
            - how many files would move, be renamed, go into _duplicates/ or Extra/;
            - ten examples of old → new names;
            - the families you renamed and why (typos, German names, foundry prefixes), the fonts in the wrong folder, the partial copies, the meaningless names, the files you couldn't read, and every case that needs my decision.
        8.4 After my OK, carry out the plan exactly and write _organise_log/moves.csv (old path, new path) as you go, plus a script _organise_log/undo that moves everything back.
        8.5 At the end, check that every file is still there (same count as at the start), that no two files share a name in the same folder and that no font is left loose at the top. Write _organise_log/report.md and tell me: families, variants and fonts after the change, what went wrong, and what is left for me to decide.
        """;

    private bool copied;

    private async Task CopyPrompt()
    {
        copied = await Bridge.CopyTextAsync(Prompt);
        StateHasChanged();
        if (!copied) return;
        await Task.Delay(2000);
        copied = false;
    }
}
