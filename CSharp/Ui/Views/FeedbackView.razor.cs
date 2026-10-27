using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

/// <summary>
/// Segnalazioni e proposte, compilate sul sito e spedite per email con Web3Forms (https://web3forms.com):
/// il sito non ha un server, così è il servizio a ricevere il modulo e a inoltrarlo alla casella collegata alla chiave.
/// </summary>
public partial class FeedbackView
{
    [Inject] private FontLibrary Lib { get; set; } = default!;
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    /// <summary>
    /// Chiave di accesso Web3Forms (si riceve gratis su web3forms.com indicando la casella che deve ricevere i messaggi).
    /// Non è un segreto: serve solo a dire a Web3Forms a chi inoltrare, quindi può stare nel codice del sito.
    /// </summary>
    private const string Web3FormsKey = "655b1bce-af88-419c-be0d-870a5f83e4e4";

    private string title = "", message = "", steps = "", email = "";
    private const string StepsHint = "1. Open the folder…\n2. Click…";   // a capo vero: in Razor &#10; si vedrebbe scritto
    private bool includeInfo = true, botcheck, sending, sent;
    private string? error;

    private bool IsBug => Lib.Feedback == FeedbackKind.Bug;

    private string InfoPreview
    {
        get
        {
            var folder = Lib.IsDemo ? "no folder open" : $"{Lib.Families.Count} families";
            return $"browser and window size, the page you were on, {folder}";
        }
    }

    private async Task SendAsync()
    {
        error = null;
        if (botcheck) { sent = true; return; }   // compilato da un programma: si finge l'invio
        if (Web3FormsKey.Length == 0)
        {
            error = "Sending isn’t set up yet (missing Web3Forms access key).";
            return;
        }
        sending = true;
        StateHasChanged();
        var kind = IsBug ? "Bug" : "Idea";
        var body = $"{(IsBug ? "What happened" : "Idea")}:\n{message.Trim()}\n";
        if (IsBug && steps.Trim().Length > 0) body += $"\nSteps:\n{steps.Trim()}\n";
        if (email.Trim().Length > 0) body += $"\nReply to: {email.Trim()}\n";
        if (includeInfo)
        {
            var styles = Lib.IsDemo ? 0 : Lib.Families.Values.Sum(f => f.Count(true));
            body += $"\n—\nPage: {Lib.ViewBeforeSettings}\nFolder: {(Lib.IsDemo ? "none" : $"{Lib.Families.Count} families, {styles} styles")}\n";
        }
        var result = await Bridge.SendFormAsync(Web3FormsKey, new FeedbackForm(
            $"before font book · {kind}: {title.Trim()}", "before font book", email.Trim().Length > 0 ? email.Trim() : null, body), includeInfo);
        sending = false;
        if (result.Ok) sent = true;
        else error = $"The message couldn’t be sent ({result.Message}). Please try again in a moment.";
    }

    private void Reset()
    {
        title = message = steps = "";
        sent = false;
        error = null;
    }
}
