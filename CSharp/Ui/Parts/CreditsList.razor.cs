using Bfontb.Models;
using Bfontb.Services;
using Microsoft.AspNetCore.Components;

namespace Bfontb.Ui.Parts;

public partial class CreditsList
{
    [Parameter, EditorRequired] public FontStyle Style { get; set; } = default!;
    [Inject] private FontLibrary Lib { get; set; } = default!;

    private sealed record Item(string Label, string Text, string? Url = null, bool Long = false);

    private IEnumerable<Item> Items
    {
        get
        {
            var info = Style.Info;
            if (info is null)
            {
                yield return new Item("Status", Style.Status == LoadStatus.Error
                    ? "The file doesn’t open in the browser, so its credits can’t be read."
                    : !Style.IsDemo && !Lib.Connected
                        ? "Credits appear when you reconnect the folder."
                        : "Credits appear as soon as the file has been read.");
                yield break;
            }
            if (info.Note is not null) { yield return new Item("Note", OpenTypeReader.WoffNote); yield break; }

            var name = info.Full ?? string.Join(' ', new[] { info.Family, info.Sub }.Where(s => !string.IsNullOrEmpty(s)));
            foreach (var it in Plain("Name in file", name)) yield return it;
            foreach (var it in Plain("Designer", info.Designer)) yield return it;
            foreach (var it in Plain("Foundry", info.Foundry ?? (info.FoundryGuess is null ? null : info.FoundryGuess + " (from the copyright)"))) yield return it;
            foreach (var it in Plain("Year", info.Year)) yield return it;
            foreach (var it in Plain("Copyright", info.Copyright, true)) yield return it;
            foreach (var it in Plain("Trademark", info.Trademark, true)) yield return it;
            foreach (var it in Plain("Version", info.Version)) yield return it;
            foreach (var it in Link("Foundry site", info.VendorUrl)) yield return it;
            foreach (var it in Link("Designer site", info.DesignerUrl)) yield return it;
            foreach (var it in Link("License", info.LicenseUrl)) yield return it;
            foreach (var it in Plain("License text", info.License, true)) yield return it;
            foreach (var it in Plain("Description", info.Description, true)) yield return it;

            var tech = new List<string>();
            if (info.WeightClass is int w) tech.Add($"weight {w}");
            if (info.WidthClass is int wc) tech.Add("width " + (FontInfo.WidthNames.TryGetValue(wc, out var wn) ? wn : wc.ToString()));
            if (info.Glyphs is int g) tech.Add($"{Fmt.N(g)} glyphs");
            if (!string.IsNullOrEmpty(info.Vendor)) tech.Add($"vendor ID {info.Vendor}");
            if (Style.Size > 0) tech.Add(Fmt.Kb(Style.Size));
            if (tech.Count > 0) yield return new Item("Technical data", string.Join(" · ", tech));
        }
    }

    private static IEnumerable<Item> Plain(string label, string? text, bool isLong = false)
    {
        if (!string.IsNullOrWhiteSpace(text)) yield return new Item(label, text, null, isLong);
    }

    private static IEnumerable<Item> Link(string label, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) yield return new Item(label, text, Fmt.UrlOf(text));
    }
}
