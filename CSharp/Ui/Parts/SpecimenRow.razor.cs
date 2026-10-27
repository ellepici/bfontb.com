using System.Globalization;
using Bfontb.Models;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>Una riga di prova dell'elenco: nome, crediti, stella e il testo nel font (caricato quando serve, vedi LazyFont).</summary>
public partial class SpecimenRow
{
    [Parameter, EditorRequired] public FontStyle Style { get; set; } = default!;
    [Parameter, EditorRequired] public string Family { get; set; } = "";
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Extra { get; set; }
    [Parameter] public bool FamilyLink { get; set; }

    /// <summary>Il testo del collegamento alla famiglia (la variante, mentre si cerca); null = il nome della famiglia.</summary>
    [Parameter] public string? LinkText { get; set; }

    /// <summary>La sezione della pagina della famiglia a cui porta il collegamento (la variante).</summary>
    [Parameter] public string? Anchor { get; set; }

    /// <summary>Nel catalogo: gli stili della variante tra cui scegliere quello da mostrare (Regular, Book, Bold…).</summary>
    [Parameter] public IReadOnlyList<DropdownOption>? StyleOptions { get; set; }

    /// <summary>Scelto un altro stile nella tendina (il suo percorso).</summary>
    [Parameter] public EventCallback<string> OnStyle { get; set; }

    protected override FontStyle LazyStyle => Style;

    private const string ErrorText = "This file doesn’t open in the browser (unsupported format or damaged).";

    private string PinnedAttr => Lib.Pins.Contains(Style.Path) ? "true" : "false";

    private string SpecClass => LoadClass;

    private string SpecStyle => Style.Status == LoadStatus.Error
        ? ""
        : $"{Style.Css};font-size:{Lib.Size}px;letter-spacing:{(Lib.Tracking / 1000.0).ToString(CultureInfo.InvariantCulture)}em";

    private string SpecText => Style.Status == LoadStatus.Error ? ErrorText : Lib.TextFor(Family);

    private void Open()
    {
        Lib.OpenStyle(Style, Family);
    }
}
