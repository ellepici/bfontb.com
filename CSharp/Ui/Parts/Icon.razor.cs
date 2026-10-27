using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>Icona Phosphor per nome (es. "star", "star-fill", "caret-down").</summary>
public partial class Icon
{
    [Parameter, EditorRequired] public string Name { get; set; } = "";
    [Parameter] public string? Class { get; set; }
}
