using Microsoft.Playwright;
using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// How the vendored html-to-image script gets onto the page, including when that first goes wrong.
/// Each test runs in its own browser context, because request routing changes how the page loads and
/// would leak into every other test sharing <see cref="DemoFixture.Page"/>.
/// </summary>
[Collection(DemoCollectionDefinition.Name)]
public sealed class LibraryLoadingTests
{
    private const string LibraryScriptGlob = "**/_content/Kebechet.Blazor.HtmlToImage/html-to-image.js";

    private readonly DemoFixture _fixture;

    public LibraryLoadingTests(DemoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Capture_AfterTheLibraryScriptFailedToLoadOnce_LoadsItAgainAndSucceeds()
    {
        // Arrange
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var libraryRequestCount = 0;
        await page.RouteAsync(LibraryScriptGlob, async route =>
        {
            // The first request fails the way a flaky mobile connection does on first paint.
            if (Interlocked.Increment(ref libraryRequestCount) == 1)
            {
                await route.AbortAsync();
                return;
            }

            await route.ContinueAsync();
        });
        await page.GotoAsync(
            $"{_fixture.BaseUrl}/iframe.html?viewMode=story&id=capture-htmltoimage--png",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var canvas = page.Locator("body");
        await canvas.GetByTestId("png-capture").WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });

        // Act
        await canvas.GetByTestId("png-capture").ClickAsync();
        var state = await StoryState.WaitForAsync(canvas, "png-state", StoryState.IsFinished, "the png story to finish capturing");

        // Assert
        // The initializer's load failed once. Without a retry, every capture for the rest of the
        // page's life awaits that same rejected load and fails.
        Assert.True(StoryState.Bool(state, "hasCaptured"), $"Expected the capture to succeed after a failed first load. State: {state}");
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        Assert.Equal(2, libraryRequestCount);
    }
}
