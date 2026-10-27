using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Carica i font nel browser solo quando servono, al massimo 6 alla volta (con iCloud ogni lettura può scaricare il file).
/// <para>
/// Le richieste non vengono servite in ordine di arrivo: prima i testi già sullo schermo, poi quelli che stanno per
/// arrivarci. Chi esce dalla zona vicina allo schermo prima di partire viene tolto dalla coda, così scorrendo veloce
/// non si accumulano centinaia di font che nessuno sta più guardando.
/// </para>
/// <para>
/// Mentre si trascina la barra di scorrimento le righe passano sullo schermo per pochi istanti: un font parte solo
/// se la sua riga resta vicina allo schermo per <see cref="Settle"/>. Se una riga esce mentre il suo font si sta già
/// caricando, il posto torna libero subito (il file finisce di arrivare per conto suo) e passa avanti chi è sullo schermo adesso.
/// </para>
/// </summary>
public sealed class FontLoader(BrowserBridge bridge)
{
    /// <summary>Priorità di una richiesta: più alta, prima parte.</summary>
    public const int Near = 0, OnScreen = 1, Asked = 2;

    private const int Slots = 6;

    /// <summary>Posti che i font vicini allo schermo (ma non visibili) lasciano sempre liberi per quelli sullo schermo.</summary>
    private const int Reserved = 2;

    /// <summary>Quanti caricamenti abbandonati possono finire per conto loro, oltre ai 6 posti.</summary>
    private const int MaxAbandoned = 12;

    /// <summary>Quanto una riga deve restare vicina allo schermo prima che il suo font parta.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    private sealed class Job(FontStyle style)
    {
        public FontStyle Style { get; } = style;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Priority;
        public long Order;
        public bool Started;
        public int RunningAs;
        public bool Abandoned;
        public bool Full;
        public DateTime Requested = DateTime.UtcNow;
    }

    private readonly Dictionary<int, Job> _jobs = [];
    private int _active, _abandoned;
    private bool _timer;
    private long _order;
    private TaskCompletionSource _idle = Done();

    /// <summary>Quanti font aspettano o si stanno caricando.</summary>
    public int Pending => _jobs.Count;

    /// <summary>
    /// Chiede di caricare uno stile. <paramref name="full"/> vuole anche i testi lunghi dei crediti
    /// (licenza, copyright), che l'analisi della cartella conserva solo accorciati.
    /// </summary>
    public Task EnsureAsync(FontStyle st, int priority = Asked, bool full = false)
    {
        if (st.IsDemo || st.File is null || st.File.Id < 0 || st.Status == LoadStatus.Error) return Task.CompletedTask;
        if (st.Status == LoadStatus.Ready && (!full || st.Info is { Slim: false })) return Task.CompletedTask;

        if (_jobs.TryGetValue(st.Id, out var job))
        {
            if (priority > job.Priority) job.Priority = priority;
            if (job.Started && priority > job.RunningAs) job.RunningAs = priority;
            job.Full |= full;
            return job.Done.Task;
        }

        job = new Job(st) { Priority = priority, Order = ++_order, Full = full };
        _jobs[st.Id] = job;
        if (st.Status != LoadStatus.Ready) st.Status = LoadStatus.Loading;
        if (_idle.Task.IsCompleted) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Schedule();
        return job.Done.Task;
    }

    /// <summary>Lo stile non serve più (è uscito dalla zona vicina allo schermo): se non è ancora partito, lo togliamo dalla coda.</summary>
    public void Drop(FontStyle st)
    {
        if (!_jobs.TryGetValue(st.Id, out var job) || job.Priority >= Asked) return;
        if (job.Started)
        {
            // già partito: non si può fermare la lettura del file, ma il posto va a chi è sullo schermo adesso
            if (job.Abandoned || _abandoned >= MaxAbandoned) return;
            job.Abandoned = true;
            _abandoned++;
            _active--;
            Schedule();
            return;
        }
        _jobs.Remove(st.Id);
        if (st.Status == LoadStatus.Loading) st.Status = LoadStatus.Idle;
        job.Done.TrySetResult();
        CheckIdle();
    }

    /// <summary>Lo stile è uscito dallo schermo ma è ancora vicino: non deve più passare davanti a chi è sullo schermo adesso.</summary>
    public void Lower(FontStyle st)
    {
        if (_jobs.TryGetValue(st.Id, out var job) && !job.Started && job.Priority == OnScreen) job.Priority = Near;
    }

    /// <summary>Si completa quando non c'è nessun font da caricare: l'analisi della cartella lascia passare prima lo schermo.</summary>
    public Task WhenIdleAsync() => _idle.Task;

    private void Pump()
    {
        bool waiting = false;
        while (true)
        {
            var now = DateTime.UtcNow;
            Job? next = null;
            waiting = false;
            foreach (var j in _jobs.Values)
            {
                if (j.Started) continue;
                // le righe che stanno solo passando (barra trascinata, scorrimento veloce) aspettano un attimo
                if (j.Priority < Asked && now - j.Requested < Settle) { waiting = true; continue; }
                if (next is null || j.Priority > next.Priority || (j.Priority == next.Priority && j.Order < next.Order)) next = j;
            }
            if (next is null) break;
            // i font solo vicini allo schermo lasciano liberi gli ultimi posti
            if (next.Priority == Near && _active >= Slots - Reserved) break;
            // tutto occupato: un font sullo schermo prende il posto di uno che è solo vicino
            if (_active >= Slots && !(next.Priority >= OnScreen && YieldNearSlot())) break;
            next.Started = true;
            next.RunningAs = next.Priority;
            _active++;
            _ = RunAsync(next);
        }
        if (waiting) Schedule(Settle / 3);
    }

    /// <summary>Libera il posto dell'ultimo font partito tra quelli solo vicini allo schermo (il file finisce di arrivare per conto suo).</summary>
    private bool YieldNearSlot()
    {
        if (_abandoned >= MaxAbandoned) return false;
        Job? victim = null;
        foreach (var j in _jobs.Values)
            if (j.Started && !j.Abandoned && j.RunningAs == Near && (victim is null || j.Order > victim.Order)) victim = j;
        if (victim is null) return false;
        victim.Abandoned = true;
        _abandoned++;
        _active--;
        return true;
    }

    /// <summary>
    /// Decide chi parte al prossimo fotogramma, non subito: quando molte righe escono insieme dallo schermo
    /// le loro notifiche arrivano una alla volta, e così un posto liberato non va a una riga che sta per uscire anche lei.
    /// </summary>
    private void Schedule(TimeSpan? after = null)
    {
        if (_timer) return;
        _timer = true;
        _ = PumpLaterAsync(after ?? TimeSpan.FromMilliseconds(16));
    }

    private async Task PumpLaterAsync(TimeSpan after)
    {
        await Task.Delay(after);
        _timer = false;
        Pump();
    }

    private async Task RunAsync(Job job)
    {
        var st = job.Style;
        try
        {
            // i byte passano al C# solo se mancano i dati del file (o se servono i testi lunghi dei crediti)
            bool needInfo = st.Info is null || (job.Full && st.Info.Slim);
            if (st.Status != LoadStatus.Ready)
            {
                var face = "pf" + st.Id;
                bool ok = await bridge.LoadFaceAsync(st.File!.Id, face, needInfo);
                st.FaceName = face;
                st.Status = ok ? LoadStatus.Ready : LoadStatus.Error;
                if (needInfo) Parse(st, await bridge.TakeBytesAsync(st.File.Id));
            }
            else if (needInfo)
            {
                Parse(st, await bridge.ReadBytesAsync(st.File!.Id));
            }
        }
        catch
        {
            st.Status = LoadStatus.Error;
        }
        finally
        {
            if (job.Abandoned) _abandoned--; else _active--;
            _jobs.Remove(st.Id);
            job.Done.TrySetResult();
            Schedule();
            CheckIdle();
        }
    }

    private static void Parse(FontStyle st, byte[] bytes)
    {
        if (bytes.Length == 0) return;
        try { st.Info = OpenTypeReader.Read(bytes); } catch { /* restano i dati che avevamo */ }
    }

    private void CheckIdle()
    {
        if (_jobs.Count == 0) _idle.TrySetResult();
    }

    private static TaskCompletionSource Done()
    {
        var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        t.SetResult();
        return t;
    }

    /// <summary>Svuota la coda quando si apre un'altra cartella.</summary>
    public void Reset()
    {
        foreach (var j in _jobs.Values.Where(j => !j.Started).ToList())
        {
            _jobs.Remove(j.Style.Id);
            j.Done.TrySetResult();
        }
        CheckIdle();
    }
}
