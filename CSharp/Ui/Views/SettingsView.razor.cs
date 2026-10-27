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

    /// <summary>Il prompt da dare a un'AI che può leggere e spostare i file: chiede un piano da approvare e un registro per tornare indietro.</summary>
    private const string Prompt = """
        You have access to my font folder. Reorganise it so that every typeface family has its own folder and every file has a readable, uniform name, following the rules below. This is my working library: be careful, only move and rename files, never delete or modify their contents.

        TARGET STRUCTURE
        <font folder>/
          <Family>/                one folder per family, e.g. "ABC Marfa", "Adobe Garamond"
            <font files>           if the family has a single variant, its files go straight here
            <Family Variant>/      if it has several variants, one subfolder each (the main one too), full name: "ABC Marfa", "ABC Marfa Mono", "ABC Marfa Variable"
            Web/                   the .woff and .woff2 files of that family
            Extra/                 licences, PDFs, specimens, images and any file that is not a font
          _duplicates/             duplicates set aside, keeping their original relative path
          _organise_log/           your plan and your log

        RULES
        1. Identify each font from the data inside the file, not from its file or folder name: read the name table (typographic family, ID 16, or family, ID 1; subfamily IDs 17 or 2; foundry and designer, IDs 8 and 9), for example with fontTools. Use the file name only for fonts that can't be read.
        2. Group by family, not by foundry. Everything that shares the family name goes in the same family folder: "ABC Marfa Mono" and "ABC Marfa Semi Mono" belong in "ABC Marfa".
        3. Variants are widths (Condensed, Compressed, Narrow, Extended, Wide…), optical sizes (Text, Display, Caption, Subhead, Micro…), sub-families (Mono, Semi Mono, Rounded, Stencil…) and variable fonts (their own "<Family> Variable" subfolder). Weights and italics (Thin, Light, Regular, Bold, Black, Italic, Oblique…) are NOT variants: they stay together in the same folder.
        4. Folder names: the family name in normal capitalisation, as the foundry writes it. No foundry prefix unless it is part of the name, no version numbers, dates, "Trial", "Demo", "OTF", "TTF", "Desktop" or "Fonts".
        5. File names: rename every font file as <Family>-<Weight>[-Italic][-Trial].<ext>, built from the names inside the font, so that all names are readable and uniform:
           - <Family> is the full name of the variant without spaces, keeping its capitals: "ABC Marfa" → "ABCMarfa", "ABC Marfa Mono" → "ABCMarfaMono", "ABC Asfalt Condensed" → "ABCAsfaltCondensed". Variable fonts: "<Family>-Variable", e.g. "ABCMarfa-Variable.ttf".
           - <Weight> is the weight in one CamelCase word: Thin, ExtraLight, Light, Regular, Book, Medium, SemiBold, Bold, ExtraBold, Black (keep the font's own word, such as Roman or Heavy, if it uses one).
           - "-Italic" only for italics (or "-Oblique" if the font calls it so), always after the weight: "Agipo-Regular-Italic.otf", "Agipo-Bold-Italic.otf".
           - "-Trial" or "-Demo" stays at the end for trial and demo fonts, so they remain recognisable.
           - Keep the extension, in lower case. If two files would get the same name, treat them as duplicates (rule 6); if they are not the same font, don't rename them and list them in the report.
        6. Duplicates: identical files, copies like "Regular (2).otf", or the same font both as .otf and .ttf. Keep one in place (prefer .otf, then the most recent version) and move the others into _duplicates/.
        7. Files you can't place or name with confidence: leave them where they are and list them in the report.
        8. Remove folders that end up empty, and list them in the report.

        PROCESS
        1. Scan the whole folder first. Write the plan to _organise_log/plan.csv (current path, new path, reason) and show me a summary: how many families and variants, how many files would move or be renamed (with a few examples of old → new names), the duplicates and the doubtful cases. Wait for my OK before moving or renaming anything.
        2. After my OK, move and rename the files as planned and write _organise_log/moves.csv (old path, new path), so every change can be undone.
        3. At the end, tell me how many families, variants and fonts there are, and list anything that went wrong or needs my decision.
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
