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

    /// <summary>Nella pagina di una famiglia: autore e anno già scritti accanto al nome della famiglia (uguali per tutti), qui non si ripetono.</summary>
    [Parameter] public bool HideAuthor { get; set; }
    [Parameter] public bool HideYear { get; set; }
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

    /// <summary>Nel catalogo e nella pagina della famiglia: i font variabili si animano lungo l'asse del peso (vedi CanAnimate).</summary>
    [Parameter] public bool AnimateWeight { get; set; }

    /// <summary>Nel catalogo: un variabile con il corsivo dentro (slnt/ital) si mostra inclinato (scelto in tendina, o con Italic acceso).</summary>
    [Parameter] public bool Slant { get; set; }

    // la scelta attuale nella tendina: il percorso, più il segno del corsivo se il variabile è inclinato
    private string PickValue => Style.Path + (Slant && Style.SlantsToItalic ? FontStyle.ItalicMark : "");

    private string? SlantSetting => Slant && Style.Status == LoadStatus.Ready ? Style.ItalicVariation : null;

    private bool paused;

    private bool CanAnimate => AnimateWeight && Style.Status == LoadStatus.Ready && Style.Info is { WghtMin: not null, WghtMax: not null };

    // nei font variabili l'intervallo del peso letto dal file prende il posto del valore unico ("weight 300–700")
    private string? ExtraText => CanAnimate && Extra is not null
        ? Extra.Replace($"weight ~{Style.Weight}", $"weight {Style.Info!.WghtMin:0}–{Style.Info.WghtMax:0}")
        : Extra;

    private string AnimClass => CanAnimate ? (paused ? " vf-anim paused" : " vf-anim") : "";

    private string AnimStyle => CanAnimate
        ? $";--wmin:{Style.Info!.WghtMin!.Value.ToString(CultureInfo.InvariantCulture)};--wmax:{Style.Info.WghtMax!.Value.ToString(CultureInfo.InvariantCulture)}" + SlantStyle
        : SlantStyle;

    // il corsivo del variabile: fermo nell'impostazione del testo, e nell'animazione insieme al peso (--vfx, vedi specimens.css)
    private string SlantStyle => SlantSetting is { } v ? $";font-variation-settings:{v};--vfx:, {v}" : "";

    private const string ErrorText = "This file doesn’t open in the browser (unsupported format or damaged).";


    private string SpecClass => Lib.SampleIsCharacters && Style.Status != LoadStatus.Error ? LoadClass + " chars" : LoadClass;

    private string SpecStyle => Style.Status == LoadStatus.Error
        ? ""
        : $"{Style.Css};font-size:{Lib.Size}px;letter-spacing:{(Lib.Tracking / 1000.0).ToString(CultureInfo.InvariantCulture)}em";

    private string SpecText => Style.Status == LoadStatus.Error ? ErrorText : Lib.TextFor(Family);

    private void Open()
    {
        Lib.OpenStyle(Style, Family);
    }
}
