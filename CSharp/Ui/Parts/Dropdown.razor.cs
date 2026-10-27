using System.Globalization;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Bfontb.Ui.Parts;

/// <summary>Una scelta della tendina: il valore, il testo e, se serve, una nota in grigio a destra (es. "8 styles").</summary>
public sealed record DropdownOption(string Value, string Label, string? Note = null);

/// <summary>
/// Tendina nello stile del sito, al posto del &lt;select&gt; di sistema. Si usa con il mouse e con la tastiera:
/// frecce, Home/Fine, Invio o spazio per scegliere, Esc per chiudere, una lettera per saltare alla prima scelta che inizia così.
/// Con <see cref="Editable"/> si può anche scrivere un valore che non è fra le scelte (Invio o uscendo dal campo per confermarlo).
/// </summary>
public partial class Dropdown
{
    [Inject] private BrowserBridge Bridge { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<DropdownOption> Options { get; set; } = [];
    [Parameter] public string Value { get; set; } = "";
    [Parameter] public EventCallback<string> OnChange { get; set; }
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Avvisa anche quando si sceglie la voce già scelta (es. "Catalog" dalla pagina di una famiglia: si torna all'elenco).</summary>
    [Parameter] public bool NotifySame { get; set; }

    /// <summary>Id dell'etichetta visibile (es. "Size"): chi usa un lettore di schermo sente "Size, 48 pt".</summary>
    [Parameter] public string? LabelledBy { get; set; }

    /// <summary>Classi in più: "lg" per la versione grande della pagina dello stile.</summary>
    [Parameter] public string? Class { get; set; }

    // ---- versione modificabile ----

    [Parameter] public bool Editable { get; set; }

    /// <summary>Dal testo scritto al valore (già nei limiti); null se non vuol dire niente: si torna al valore di prima.</summary>
    [Parameter] public Func<string, string?>? Parse { get; set; }

    /// <summary>Come scrivere il valore nel campo (es. "+25"); di norma il valore stesso.</summary>
    [Parameter] public Func<string, string>? Format { get; set; }

    /// <summary>Unità scritta dopo il campo, in grigio (es. "pt").</summary>
    [Parameter] public string? Suffix { get; set; }

    [Parameter] public string InputWidth { get; set; } = "3em";
    [Parameter] public string InputMode { get; set; } = "text";

    private static int counter;
    private readonly string uid = $"dd{Interlocked.Increment(ref counter)}";

    private bool open;
    private int active;
    private string text = "";
    private ElementReference btn, input, list;
    private bool focusList, focusField, reveal;

    // Scelta fatta con Invio o spazio: il tasto, finito sul pulsante che riprende il fuoco, genera un clic che riaprirebbe la tendina
    private bool swallowKeyClick;

    private string CssClass => "dd" + (Editable ? " editable" : "") + (Class is null ? "" : " " + Class) + (open ? " open" : "");
    private string OptionId(int i) => $"{uid}-{i}";
    private string OptionClass(int i, DropdownOption o) =>
        "dd-opt" + (i == active ? " act" : "") + (o.Value == Value ? " sel" : "");

    private string CurrentLabel => Options.FirstOrDefault(o => o.Value == Value)?.Label ?? Value;
    private string Formatted => Format?.Invoke(Value) ?? Value;

    protected override void OnParametersSet()
    {
        if (Editable) text = Formatted;
    }

    /// <summary>La scelta da evidenziare all'apertura: quella uguale al valore o, per i numeri scritti a mano, la più vicina.</summary>
    private int StartIndex()
    {
        for (int i = 0; i < Options.Count; i++) if (Options[i].Value == Value) return i;
        if (!double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
        int best = 0;
        double dist = double.MaxValue;
        for (int i = 0; i < Options.Count; i++)
            if (double.TryParse(Options[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) && Math.Abs(o - v) < dist)
                (best, dist) = (i, Math.Abs(o - v));
        return best;
    }

    private void Toggle(MouseEventArgs e)
    {
        if (Disabled) return;
        if (swallowKeyClick && e.Detail == 0) { swallowKeyClick = false; return; }   // detail 0: clic nato dalla tastiera
        if (open) Close(true);
        else OpenList();
    }

    private void OpenList()
    {
        open = true;
        active = StartIndex();
        focusList = true;
    }

    private void Close(bool backToField)
    {
        if (!open) return;
        open = false;
        focusField = backToField;
    }

    private void Hover(int i) => active = i;

    private async Task Pick(int i)
    {
        if (i < 0 || i >= Options.Count) return;
        var v = Options[i].Value;
        Close(true);
        await Commit(v);
    }

    private async Task Commit(string v)
    {
        if (v != Value || NotifySame) await OnChange.InvokeAsync(v);
        else if (Editable) text = Formatted;
    }

    private void OnTyping(ChangeEventArgs e) => text = e.Value?.ToString() ?? "";

    /// <summary>Invio o uscita dal campo: il valore scritto, se ha senso, altrimenti si torna a quello di prima.</summary>
    private async Task OnCommit(ChangeEventArgs e)
    {
        text = e.Value?.ToString() ?? "";
        var v = Parse?.Invoke(text);
        if (v is null) { text = Formatted; return; }
        await Commit(v);
        text = Format?.Invoke(v) ?? v;
    }

    private void OnFieldKey(KeyboardEventArgs e)
    {
        swallowKeyClick = false;
        if (!open && !Disabled && (e.Key is "ArrowDown" || (!Editable && e.Key is "ArrowUp"))) OpenList();
    }

    private async Task OnListKey(KeyboardEventArgs e)
    {
        int last = Options.Count - 1;
        switch (e.Key)
        {
            case "ArrowDown": active = Math.Min(last, active + 1); break;
            case "ArrowUp": active = Math.Max(0, active - 1); break;
            case "Home": case "PageUp": active = 0; break;
            case "End": case "PageDown": active = last; break;
            case "Enter": case " ": swallowKeyClick = true; await Pick(active); return;
            case "Escape": case "Tab": swallowKeyClick = true; Close(true); return;
            default:
                if (e.Key.Length == 1 && !e.CtrlKey && !e.MetaKey) Jump(e.Key);
                break;
        }
        reveal = true;
    }

    /// <summary>Una lettera (o una cifra): la prossima scelta che inizia così.</summary>
    private void Jump(string key)
    {
        for (int n = 1; n <= Options.Count; n++)
        {
            int i = (active + n) % Options.Count;
            if (Options[i].Label.StartsWith(key, StringComparison.OrdinalIgnoreCase)) { active = i; return; }
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (focusList && open)
            {
                focusList = false;
                await Bridge.PlaceListAsync(list);
                await list.FocusAsync(preventScroll: true);
                await Bridge.RevealOptionAsync(list, active);
            }
            else if (reveal && open)
            {
                reveal = false;
                await Bridge.RevealOptionAsync(list, active);
            }
            if (focusField && !open)
            {
                focusField = false;
                await (Editable ? input : btn).FocusAsync(preventScroll: true);
            }
        }
        catch { /* l'elemento non c'è più (pagina cambiata): niente da fare */ }
    }
}
