using Bfontb.Services;
using Bfontb.Ui.Views;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

public partial class MadreBlock
{
    [Inject] private FontLibrary Lib { get; set; } = default!;

    [Parameter, EditorRequired] public PrepView.MadreRow Madre { get; set; } = default!;

    private string? mergeTarget;
    private readonly Dictionary<string, string?> moveTargets = [];

    private void SetMoveTarget(string variant, ChangeEventArgs e) => moveTargets[variant] = e.Value?.ToString();

    /// <summary>Cambia il nome: tutto ciò che oggi finisce in questa famiglia finirà nel nuovo nome.</summary>
    private async Task Rename(ChangeEventArgs e)
    {
        var name = e.Value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name) || name == Madre.Name) return;
        if (Lib.Fix.Rename.GetValueOrDefault(name) == Madre.Name) Lib.Fix.Rename.Remove(name);
        Lib.Fix.Rename[Madre.Name] = name;
        await Lib.ApplyChangesAsync();
    }

    private async Task Merge()
    {
        var target = mergeTarget?.Trim();
        if (string.IsNullOrEmpty(target) || target == Madre.Name) return;
        Lib.Fix.Merge[Madre.Name] = target;
        mergeTarget = null;
        await Lib.ApplyChangesAsync();
    }

    /// <summary>Sposta una variante in un'altra famiglia (o in una famiglia nuova con il suo nome, per "Separa").</summary>
    private async Task MoveTo(PrepView.VariantRow v, string? destination)
    {
        destination = destination?.Trim();
        if (string.IsNullOrEmpty(destination)) return;
        foreach (var key in v.Keys) Lib.Fix.Move[key] = destination;
        moveTargets.Remove(v.Name);
        await Lib.ApplyChangesAsync();
    }
}
