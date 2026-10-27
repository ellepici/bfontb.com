using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

public partial class SearchLinks
{
    [Parameter, EditorRequired] public string Family { get; set; } = "";
    [Parameter] public FontInfo? Info { get; set; }

    private IEnumerable<(string Label, string Url)> Links
    {
        get
        {
            var q = Uri.EscapeDataString(Family);
            var who = Info?.Foundry ?? Info?.Designer ?? "";
            yield return ("Fonts In Use", $"https://fontsinuse.com/search?terms={q}");
            yield return ("MyFonts", $"https://www.myfonts.com/search?query={q}");
            yield return ("Google Fonts", $"https://fonts.google.com/?query={q}");
            yield return ("Google", $"https://www.google.com/search?q={Uri.EscapeDataString($"\"{Family}\" typeface {who}".Trim())}");
            var site = Fmt.UrlOf(Info?.VendorUrl) ?? Fmt.UrlOf(Info?.DesignerUrl);
            if (site is not null) yield return ("Foundry site", site);
        }
    }
}
