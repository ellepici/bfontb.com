using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

/// <summary>
/// La colonna di sinistra delle pagine di servizio. Lo scorrimento tra le sezioni delle impostazioni e la sezione accesa
/// li gestisce bfontb.js (settingsNav): così non serve ridisegnare la pagina a ogni movimento della rotella.
/// </summary>
public partial class SettingsNav : IAsyncDisposable
{
    [Inject] private BrowserBridge Bridge { get; set; } = default!;
    [Inject] private FontLibrary Lib { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    private ElementReference navEl;
    private View? boundFor;

    private string? goTo;   // la sezione da raggiungere appena le impostazioni sono aperte

    private string? Here(View page) => Lib.View == page ? "here" : null;

    // le sezioni che la pagina delle impostazioni mostra adesso: senza cartella non c'è la preparazione,
    // senza proposta non c'è l'elenco delle famiglie proposte
    private bool Shown(string id) => id switch
    {
        "prep-proposal" => !Lib.IsDemo && Lib.Proposal is not null,
        _ when id.StartsWith("prep-") => !Lib.IsDemo,
        _ => true,
    };

    // dalle altre pagine si aprono le impostazioni e poi si scende alla sezione; nelle impostazioni ci pensa bfontb.js
    private void GoTo(string id)
    {
        if (Lib.View == View.Settings) return;
        goTo = id;
        Lib.OpenPage(View.Settings);
    }
    private string? HereFeedback(FeedbackKind kind) => Lib.View == View.Feedback && Lib.Feedback == kind ? "here" : null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // a ogni cambio di pagina si ricollega l'indice: solo nelle impostazioni segue lo scorrimento
        if (boundFor == Lib.View) return;
        boundFor = Lib.View;
        var target = goTo; goTo = null;
        try { await Bridge.SettingsNavAsync(navEl, Lib.View == View.Settings, target); } catch { /* pagina cambiata */ }
    }

    public async ValueTask DisposeAsync()
    {
        try { await Bridge.StopSettingsNavAsync(); } catch { /* pagina chiusa */ }
    }
}
