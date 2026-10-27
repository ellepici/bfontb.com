using System.Text.Json;
using Bfontb.Models;

namespace Bfontb.Services;

/// <summary>Salva i risultati dell'analisi nel browser (IndexedDB), così una cartella si legge una volta sola.</summary>
public sealed class AnalysisCache(BrowserBridge bridge)
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly List<string[]> _pending = [];

    public async Task<Dictionary<string, FontInfo>> LoadAsync(string prefix)
    {
        var result = new Dictionary<string, FontInfo>(StringComparer.Ordinal);
        try
        {
            foreach (var pair in await bridge.IdbLoadAsync(prefix))
            {
                if (pair.Length != 2) continue;
                var info = JsonSerializer.Deserialize<FontInfo>(pair[1], Json);
                if (info is not null) result[pair[0]] = info;
            }
        }
        catch { /* archivio del browser non disponibile: si riparte da zero */ }
        return result;
    }

    public async Task PutAsync(string path, FontInfo info)
    {
        _pending.Add([path, JsonSerializer.Serialize(info, Json)]);
        if (_pending.Count >= 200) await FlushAsync();
    }

    public async Task FlushAsync()
    {
        if (_pending.Count == 0) return;
        var items = _pending.ToArray();
        _pending.Clear();
        try { await bridge.IdbPutAsync(items); } catch { }
    }

    /// <summary>Elenco dei file (solo percorsi e dimensioni) dell'ultima cartella aperta.</summary>
    public sealed record ListedFile(string Path, long Size);

    public async Task SaveListingAsync(string root, IEnumerable<FontFile> files)
    {
        try { await bridge.MetaSetAsync("listing/" + root, JsonSerializer.Serialize(files.Select(f => new ListedFile(f.Path, f.Size)), Json)); }
        catch { }
    }

    public async Task<List<ListedFile>?> LoadListingAsync(string root)
    {
        try
        {
            var raw = await bridge.MetaGetAsync("listing/" + root);
            return raw is null ? null : JsonSerializer.Deserialize<List<ListedFile>>(raw, Json);
        }
        catch { return null; }
    }

    public async Task ClearAsync(string prefix)
    {
        try { await bridge.IdbClearAsync(prefix); } catch { }
    }
}
