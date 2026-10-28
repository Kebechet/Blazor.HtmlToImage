using Blazor.HtmlToImage.ServerHost.Components;
using Kebechet.Blazor.HtmlToImage;

var builder = WebApplication.CreateBuilder(args);

// The browser tests run this from build output in the Production environment, where only a publish
// would have copied blazor.web.js and the package's scripts into wwwroot. This serves them from the
// build's static web assets manifest instead, as Development does by default.
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // SignalR's own default, pinned so the browser tests keep proving that captures fit through it
    // even if a future framework version raises it.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 32 * 1024);
builder.Services.AddHtmlToImage();

var app = builder.Build();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
