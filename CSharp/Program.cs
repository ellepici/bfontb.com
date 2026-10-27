using Bfontb.Services;
using Bfontb.Ui;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Un'unica istanza di ogni servizio per tutta la sessione nel browser.
builder.Services.AddSingleton<BrowserBridge>();
builder.Services.AddSingleton<FontLoader>();
builder.Services.AddSingleton<GlyphLoader>();
builder.Services.AddSingleton<AnalysisCache>();
builder.Services.AddSingleton<FontLibrary>();
builder.Services.AddSingleton<AnalysisRunner>();

await builder.Build().RunAsync();
