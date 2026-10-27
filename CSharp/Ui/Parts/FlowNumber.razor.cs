using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>
/// Numero animato con NumberFlow (libreria MIT di Maxwell Barvian, copia locale in wwwroot/js/vendor/number-flow.js).
/// Il primo valore compare e basta; ai cambi successivi le cifre scorrono verso il nuovo valore.
/// </summary>
public partial class FlowNumber
{
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    [Parameter] public long Value { get; set; }

    private ElementReference el;
    private long? shown;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (shown == Value) return;
        shown = Value;
        try { await Bridge.FlowNumberAsync(el, Value); } catch { /* pagina cambiata */ }
    }
}
