using Bfontb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Bfontb.Ui.Parts;

/// <summary>Il pulsante della cartella aperta in alto a destra, con il riquadro per cambiarla.</summary>
public partial class FolderButton
{
    [Inject] private FontLibrary Lib { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    private bool open;
    private object? openedFiles;

    private void Toggle() { open = !open; openedFiles = Lib.Files; }
    private void Close() => open = false;
    private async Task CloseSoon() { await Task.Delay(50); open = false; }   // dopo il clic che apre la scelta del sistema
    private void OnKey(KeyboardEventArgs e) { if (e.Key == "Escape") open = false; }

    // è arrivata un'altra cartella (anche con lo stesso nome): il riquadro si chiude
    protected override void OnParametersSet()
    {
        if (open && !ReferenceEquals(Lib.Files, openedFiles)) open = false;
    }
}
