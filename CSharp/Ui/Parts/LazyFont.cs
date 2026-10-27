using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bfontb.Ui.Parts;

/// <summary>
/// Base delle righe di prova e delle schede della griglia: carica il font solo quando il suo testo si avvicina
/// allo schermo, gli dà la precedenza quando è sullo schermo e lo toglie dalla coda quando se ne allontana.
/// </summary>
public abstract class LazyFont : ComponentBase, IAsyncDisposable
{
    [Inject] protected FontLibrary Lib { get; set; } = default!;
    [Inject] protected FontLoader Loader { get; set; } = default!;
    [Inject] protected BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Lo stile da caricare.</summary>
    protected abstract FontStyle LazyStyle { get; }

    /// <summary>L'elemento che contiene il testo nel font (quello osservato).</summary>
    protected ElementReference specEl;

    private DotNetObjectReference<LazyFont>? selfRef;
    private FontStyle? observed;
    private bool observing;

    protected bool Offline => !LazyStyle.IsDemo && !Lib.Connected;

    /// <summary>Classe CSS del testo: in caricamento, scollegato o non apribile.</summary>
    protected string LoadClass => LazyStyle.Status switch
    {
        LoadStatus.Error => "err",
        LoadStatus.Ready => "",
        _ => LazyStyle.IsDemo ? "" : Offline ? "offline" : "loading",
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Con Virtualize lo stesso componente può ricevere uno stile diverso: lo stile vecchio esce dalla coda e si osserva di nuovo.
        var style = LazyStyle;
        if (ReferenceEquals(observed, style)) return;
        if (observed is not null) Loader.Drop(observed);
        observed = style;
        if (style.IsDemo || Offline || style.Status is LoadStatus.Ready or LoadStatus.Error)
        {
            if (observing) { observing = false; await Bridge.UnobserveAsync(specEl); }
            if (Offline) observed = null;
            return;
        }
        if (disposed) return;
        selfRef ??= DotNetObjectReference.Create(this);
        observing = true;
        // la riga può sparire mentre si aspetta il JavaScript (scorrimento veloce, cambio di pagina): niente da osservare
        try { await Bridge.ObserveAsync(specEl, selfRef); }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
    }

    private bool disposed;

    /// <summary>Il testo entra nella zona vicina allo schermo (si mette in coda) o ne esce (si toglie dalla coda).</summary>
    [JSInvokable]
    public Task OnNear(bool near)
    {
        if (!near) { Loader.Drop(LazyStyle); return Task.CompletedTask; }
        return LoadAsync(FontLoader.Near);
    }

    /// <summary>Il testo è sullo schermo: passa davanti a quelli che stanno solo per arrivarci. Quando esce torna in fila con gli altri.</summary>
    [JSInvokable]
    public Task OnSeen(bool seen)
    {
        if (!seen) { Loader.Lower(LazyStyle); return Task.CompletedTask; }
        return LoadAsync(FontLoader.OnScreen);
    }

    private async Task LoadAsync(int priority)
    {
        var st = LazyStyle;
        if (st.Status is LoadStatus.Ready or LoadStatus.Error) return;
        await Loader.EnsureAsync(st, priority);
        if (ReferenceEquals(st, LazyStyle)) await InvokeAsync(StateHasChanged);
    }

    public virtual async ValueTask DisposeAsync()
    {
        disposed = true;
        if (observed is not null) Loader.Drop(observed);
        try { await Bridge.UnobserveAsync(specEl); } catch (JSException) { }
        selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
