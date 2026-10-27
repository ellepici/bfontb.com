using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>
/// Legge l'elenco dei caratteri di uno stile quando si apre la sua pagina e lo tiene in memoria
/// per gli ultimi stili visti (tornando indietro non si rilegge il file).
/// </summary>
public sealed class GlyphLoader(BrowserBridge bridge)
{
    private const int Keep = 12;

    private readonly Dictionary<int, Task<GlyphSet>> _sets = [];
    private readonly LinkedList<int> _recent = new();

    public Task<GlyphSet> GetAsync(FontStyle st)
    {
        if (st.File is null || st.File.Id < 0) return Task.FromResult(new GlyphSet { Error = "offline" });
        if (_sets.TryGetValue(st.Id, out var t))
        {
            _recent.Remove(st.Id);
            _recent.AddFirst(st.Id);
            return t;
        }
        t = LoadAsync(st.File.Id);
        _sets[st.Id] = t;
        _recent.AddFirst(st.Id);
        while (_recent.Count > Keep)
        {
            _sets.Remove(_recent.Last!.Value);
            _recent.RemoveLast();
        }
        return t;
    }

    private async Task<GlyphSet> LoadAsync(int fileId)
    {
        try
        {
            var bytes = await bridge.ReadBytesAsync(fileId);
            return GlyphReader.Read(bytes);
        }
        catch
        {
            return new GlyphSet { Error = "Can’t read this file." };
        }
    }

    /// <summary>Si apre un'altra cartella: gli id dei file cambiano.</summary>
    public void Reset()
    {
        _sets.Clear();
        _recent.Clear();
    }
}
