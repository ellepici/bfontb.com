using System.Globalization;
using Bfontb.Models;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>
/// Scheda della vista a griglia (il font si carica quando serve, vedi LazyFont): nel catalogo una famiglia,
/// nella pagina della famiglia uno stile (<see cref="OpensStyle"/>).
/// </summary>
public partial class FamilyCard
{
    [Parameter, EditorRequired] public TypeFamily Family { get; set; } = default!;
    [Parameter, EditorRequired] public FontStyle Style { get; set; } = default!;

    /// <summary>Sotto «Aa»: quanti stili (o quanti rispondono ai filtri).</summary>
    [Parameter] public string Note { get; set; } = "";

    /// <summary>Corpo di «Aa», in pixel (dipende dalla grandezza delle schede).</summary>
    [Parameter] public double AaSize { get; set; } = 60;

    [Parameter] public bool Current { get; set; }

    /// <summary>Scheda di uno stile: sotto c'è il nome dello stile e il clic apre la sua pagina (tutti i caratteri).</summary>
    [Parameter] public bool OpensStyle { get; set; }

    /// <summary>Nel catalogo, mentre si cerca: il nome della variante al posto di quello della famiglia.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>La sezione della variante nella pagina della famiglia, dove porta il clic.</summary>
    [Parameter] public string? Anchor { get; set; }

    /// <summary>Nel catalogo: gli stili della variante tra cui passare con le maniglie (dal più leggero), come la tendina dell'elenco.</summary>
    [Parameter] public IReadOnlyList<DropdownOption>? StyleOptions { get; set; }

    /// <summary>Scelto un altro stile con le maniglie o con le frecce (il suo percorso).</summary>
    [Parameter] public EventCallback<string> OnStyle { get; set; }

    /// <summary>Lo stile prima o dopo quello mostrato (si ricomincia dall'altro capo).</summary>
    private Task Step(int d)
    {
        if (StyleOptions is not { Count: > 1 } list) return Task.CompletedTask;
        int i = Math.Max(0, list.ToList().FindIndex(o => o.Value == Style.Path));
        return OnStyle.InvokeAsync(list[(i + d + list.Count) % list.Count].Value);
    }

    // la scheda si apre con Invio; con la scheda attiva, le frecce destra e sinistra cambiano stile
    private Task OnKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key == "Enter") Open();
        else if (e.Key == "ArrowLeft") return Step(-1);
        else if (e.Key == "ArrowRight") return Step(1);
        return Task.CompletedTask;
    }

    private string TitleText => OpensStyle
        ? $"Open the style page: {Style.Label} · {Style.FileName}"
        : $"{Title ?? Family.Name} — {Style.Label} · {Style.FileName}{(StyleOptions is { Count: > 1 } ? $" · {Note}" : "")}";

    protected override FontStyle LazyStyle => Style;

    private string AaStyle => Style.Status == LoadStatus.Error
        ? ""
        : $"{Style.Css};font-size:{AaSize.ToString("0.#", CultureInfo.InvariantCulture)}px";

    private void Open()
    {
        if (OpensStyle) Lib.OpenStyle(Style, Family.Name);
        else Lib.OpenFamily(Family.Name, Anchor);
    }
}
