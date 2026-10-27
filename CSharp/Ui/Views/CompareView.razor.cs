using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Views;

public partial class CompareView
{
    [Inject] private FontLibrary Lib { get; set; } = default!;

    /// <summary>Versione dello stato dell'app (vedi FontLibrary.Version).</summary>
    [Parameter] public int Version { get; set; }

    private List<(FontStyle Style, string Family)> Pinned = [];

    protected override void OnParametersSet()
    {
        Pinned = Lib.Families.Values
            .SelectMany(f => f.Styles(true).Where(s => Lib.Pins.Contains(s.Path)).Select(s => (s, f.Name)))
            .ToList();
    }
}
