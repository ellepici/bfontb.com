using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>Legge i dati interni di tutti i font della cartella, 6 file alla volta, con avanzamento e stop.</summary>
public sealed class AnalysisRunner(FontLibrary library, BrowserBridge bridge, AnalysisCache cache, FontLoader loader)
{
    public bool Running { get; private set; }
    public int Done { get; private set; }
    public int Total { get; private set; }
    private bool _stop;
    private DateTime _lastNotify;

    public int Percent => Total == 0 ? 100 : (int)Math.Round(Done * 100.0 / Total);

    public void Stop() => _stop = true;

    public async Task RunAsync()
    {
        if (Running || !library.Connected) return;
        var all = library.FilesToAnalyze().ToList();
        var todo = all.Where(library.NeedsAnalysis).ToList();
        Total = all.Count;
        Done = all.Count - todo.Count;
        Running = true;
        _stop = false;
        library.Notify();

        int next = -1;
        async Task Worker()
        {
            while (!_stop)
            {
                // i font da mostrare sullo schermo passano prima dell'analisi
                await loader.WhenIdleAsync();
                int i = Interlocked.Increment(ref next);
                if (i >= todo.Count) return;
                var f = todo[i];
                FontInfo info;
                try
                {
                    info = OpenTypeReader.Read(await bridge.ReadBytesAsync(f.Id)).ToSlim();
                    if (ShapeProbe.ShouldProbe(f, info))
                        info.ShapeSerif = ShapeProbe.Decide(await bridge.ProbeShapeAsync(f.Id, ShapeProbe.Glyphs, ShapeProbe.Size));
                }
                catch { info = new FontInfo { Error = true, Slim = true, Schema = FontInfo.CurrentSchema }; }
                library.Analysis[f.Path] = info;
                await cache.PutAsync(f.Path, info);
                Done++;
                // fuori dalla Preparazione l'avanzamento non si vede: aggiorniamo l'interfaccia più di rado
                var every = TimeSpan.FromMilliseconds(library.View == View.Settings ? 250 : 2000);
                if (DateTime.UtcNow - _lastNotify > every)
                {
                    _lastNotify = DateTime.UtcNow;
                    library.Notify();
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Worker()));
        await cache.FlushAsync();
        Running = false;
        await library.AnalysisFinishedAsync(!_stop);
    }
}
