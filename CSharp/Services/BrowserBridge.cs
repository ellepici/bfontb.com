using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Bfontb.Services;

/// <summary>Chiamate C# verso il modulo wwwroot/js/bfontb.js (solo API del browser, nessuna logica).</summary>
/// <summary>Il modulo di segnalazione, nei campi che si aspetta Web3Forms.</summary>
public sealed record FeedbackForm(
    [property: System.Text.Json.Serialization.JsonPropertyName("subject")] string Subject,
    [property: System.Text.Json.Serialization.JsonPropertyName("from_name")] string FromName,
    [property: System.Text.Json.Serialization.JsonPropertyName("email")] string? Email,
    [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message);

/// <summary>Esito dell'invio: riuscito, o il messaggio d'errore del servizio.</summary>
public sealed record SendResult(bool Ok, string Message);

public sealed class BrowserBridge(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;
    // Il ponte si carica con un indirizzo sempre nuovo (?v=…): così non passa dalla mappa con il controllo d'integrità di .NET,
    // che bloccava l'app ogni volta che bfontb.js cambiava senza ricompilare, e il browser non ne usa mai una copia vecchia.
    private static readonly string ModuleUrl = $"./js/bfontb.js?v={DateTime.UtcNow.Ticks}";

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", ModuleUrl).AsTask();

    public async Task InitAsync<T>(DotNetObjectReference<T> app, ElementReference input) where T : class =>
        await (await Module).InvokeVoidAsync("init", app, input);

    public async Task<bool> LoadFaceAsync(int fileId, string faceName, bool keepBytes) =>
        await (await Module).InvokeAsync<bool>("loadFace", fileId, faceName, keepBytes);

    public async Task<byte[]> TakeBytesAsync(int fileId) =>
        await (await Module).InvokeAsync<byte[]>("takeBytes", fileId);

    public async Task<byte[]> ReadBytesAsync(int fileId) =>
        await (await Module).InvokeAsync<byte[]>("readBytes", fileId);

    public async Task ObserveAsync<T>(ElementReference el, DotNetObjectReference<T> target) where T : class =>
        await (await Module).InvokeVoidAsync("observe", el, target);

    public async Task UnobserveAsync(ElementReference el)
    {
        try { await (await Module).InvokeVoidAsync("unobserve", el); } catch (JSDisconnectedException) { }
    }

    /// <param name="feature">La funzione OpenType accesa (es. 'dlig' 1), o null per un carattere codificato.</param>
    /// <param name="inkCenter">Il centro del disegno in em, letto dal file (vedi GlyphBounds), se lo si conosce.</param>
    public async Task CenterGlyphAsync(ElementReference el, string text, string fontPrefix, string family, double size, string? feature, double? inkCenter) =>
        await (await Module).InvokeVoidAsync("centerGlyph", el, text, fontPrefix, family, size, feature, inkCenter);

    public async Task WatchWidthAsync<T>(ElementReference el, DotNetObjectReference<T> target) where T : class =>
        await (await Module).InvokeVoidAsync("watchWidth", el, target);

    public async Task UnwatchWidthAsync(ElementReference el)
    {
        try { await (await Module).InvokeVoidAsync("unwatchWidth", el); } catch (JSDisconnectedException) { }
    }

    public async Task SetThemeAsync(string theme) => await (await Module).InvokeVoidAsync("setTheme", theme);

    /// <summary>Porta in cima all'indice a sinistra la famiglia aperta (vedi revealFamily in bfontb.js).</summary>
    public async Task RevealFamilyAsync() => await (await Module).InvokeVoidAsync("revealFamily");

    /// <summary>Frecce su e giù nella griglia dei caratteri (vedi glyphKeys in bfontb.js).</summary>
    public async Task GlyphKeysAsync(ElementReference root) => await (await Module).InvokeVoidAsync("glyphKeys", root);

    /// <summary>Fa vedere la cella scelta della griglia dei caratteri, se è fuori dallo schermo.</summary>
    public async Task RevealGlyphAsync(ElementReference root) => await (await Module).InvokeVoidAsync("revealGlyph", root);

    public async Task ScrollToAsync(string id) => await (await Module).InvokeVoidAsync("scrollToId", id);

    /// <summary>Come ScrollToAsync, ma dopo che la vista appena aperta è stata riportata in cima.</summary>
    public async Task ScrollToSoonAsync(string id) => await (await Module).InvokeVoidAsync("scrollToIdSoon", id);

    public async Task<SendResult> SendFormAsync(string accessKey, FeedbackForm form, bool withInfo) =>
        await (await Module).InvokeAsync<SendResult>("sendForm", accessKey, form, withInfo);

    public async Task FlowNumberAsync(ElementReference el, long value) => await (await Module).InvokeVoidAsync("flowNumber", el, value);

    public async Task SettingsNavAsync(ElementReference nav, bool active, string? goTo) => await (await Module).InvokeVoidAsync("settingsNav", nav, active, goTo);

    public async Task StopSettingsNavAsync() => await (await Module).InvokeVoidAsync("stopSettingsNav");

    public async Task<bool> CopyTextAsync(string text) =>
        await (await Module).InvokeAsync<bool>("copyText", text);

    public async Task PlaceListAsync(ElementReference list) =>
        await (await Module).InvokeVoidAsync("placeList", list);

    public async Task RevealOptionAsync(ElementReference list, int index) =>
        await (await Module).InvokeVoidAsync("revealOption", list, index);

    public async Task CenterMarksAsync(ElementReference cells, string fontPrefix, string family) =>
        await (await Module).InvokeVoidAsync("centerMarks", cells, fontPrefix, family);

    /// <summary>Nuovo indirizzo della vista (o lo stesso, sostituito) e titolo della scheda. Vedi goUrl in bfontb.js.</summary>
    public async Task GoUrlAsync(string url, string title, bool replace) => await (await Module).InvokeVoidAsync("goUrl", url, title, replace);

    public async Task SetTitleAsync(string title) => await (await Module).InvokeVoidAsync("setTitle", title);

    /// <summary>La colonna dei caratteri mostra un'altra cosa: in cima, o dove si era (vedi showView in bfontb.js).</summary>
    public async Task ShowViewAsync(string key, bool restore) => await (await Module).InvokeVoidAsync("showView", key, restore);

    public async Task ScrollTopAsync(string selector) => await (await Module).InvokeVoidAsync("scrollTop", selector);

    public async Task<int[]> ProbeShapeAsync(int fileId, string glyphs, int size) =>
        await (await Module).InvokeAsync<int[]>("probeShape", fileId, glyphs.Select(c => c.ToString()).ToArray(), size);

    public async Task<string?> GetLocalAsync(string key) => await (await Module).InvokeAsync<string?>("lsGet", key);
    public async Task SetLocalAsync(string key, string value) => await (await Module).InvokeVoidAsync("lsSet", key, value);

    public async Task<string[][]> IdbLoadAsync(string prefix) => await (await Module).InvokeAsync<string[][]>("idbLoad", prefix);
    public async Task IdbPutAsync(IEnumerable<string[]> pairs) => await (await Module).InvokeVoidAsync("idbPut", pairs);
    public async Task IdbClearAsync(string prefix) => await (await Module).InvokeVoidAsync("idbClear", prefix);

    public async Task<string?> MetaGetAsync(string key) => await (await Module).InvokeAsync<string?>("metaGet", key);
    public async Task MetaSetAsync(string key, string value) => await (await Module).InvokeVoidAsync("metaSet", key, value);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) await (await _module).DisposeAsync();
    }
}
