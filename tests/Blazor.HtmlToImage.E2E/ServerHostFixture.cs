using Microsoft.Playwright;
using Xunit;

namespace Blazor.HtmlToImage.E2E;

[CollectionDefinition(Name)]
public sealed class ServerHostCollectionDefinition : ICollectionFixture<ServerHostFixture>
{
    public const string Name = "Blazor Server host";
}

/// <summary>
/// A minimal Blazor Server app open in a real browser. Every interop result there crosses a SignalR
/// circuit whose messages are capped at 32 KB, which WebAssembly never sees.
/// </summary>
public sealed class ServerHostFixture : BrowserAppFixture
{
    protected override string ProjectPath => Path.Combine("tests", "Blazor.HtmlToImage.ServerHost", "Blazor.HtmlToImage.ServerHost.csproj");

    /// <summary>Opens the capture page and waits until its circuit is live, so its buttons do something.</summary>
    public async Task<ILocator> OpenCapturePageAsync()
    {
        ClearJsErrors();
        await Page.GotoAsync($"{BaseUrl}/?e2e={Guid.NewGuid():N}", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var body = Page.Locator("body");
        await body.GetByTestId("interactive").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 60_000
        });

        return body;
    }
}
