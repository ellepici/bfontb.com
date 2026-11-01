using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>Icona Carbon per nome, come si chiama nella libreria (es. "settings", "text--italic", "chevron--down").</summary>
public partial class Icon
{
    [Parameter, EditorRequired] public string Name { get; set; } = "";
    [Parameter] public string? Class { get; set; }
}
