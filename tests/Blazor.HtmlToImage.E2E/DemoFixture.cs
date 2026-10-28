using Microsoft.Playwright;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Blazor.HtmlToImage.E2E;

[CollectionDefinition(Name)]
public sealed class DemoCollectionDefinition : ICollectionFixture<DemoFixture>
{
    public const string Name = "BlazingStory demo";
}

/// <summary>The BlazingStory demo - a standalone Blazor WebAssembly app - open in a real browser.</summary>
public sealed class DemoFixture : BrowserAppFixture
{
    protected override string ProjectPath => Path.Combine("demo", "Blazor.HtmlToImage.Demo.csproj");

    /// <summary>
    /// Navigates straight to a story canvas, then waits until the vendored library is actually on
    /// the page.
    /// </summary>
    /// <remarks>
    /// The canvas page (<c>iframe.html</c>) is what BlazingStory's shell hosts in an iframe anyway,
    /// so addressing it directly is faster and steadier - no sidebar, no panels, no cross-frame hop.
    /// <para>
    /// Waiting on <c>globalThis.htmlToImage</c> rather than on Blazor's first paint is the point:
    /// the package injects the script from its JS initializer, and that injection is exactly what
    /// these tests exist to prove. A capture fired before it resolves would fail for a reason that
    /// has nothing to do with the wrapper.
    /// </para>
    /// </remarks>
    public async Task<ILocator> NavigateToStoryAsync(string storyId)
    {
        ClearJsErrors();
        var url = $"{BaseUrl}/iframe.html?viewMode=story&id={storyId}&e2e={Guid.NewGuid():N}";
        await Page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await Page.WaitForFunctionAsync(
            "() => typeof globalThis.htmlToImage !== 'undefined'",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        return Page.Locator("body");
    }

    /// <summary>Clicks a story's Capture button and waits for the panel to report a finished capture.</summary>
    public async Task<System.Text.Json.JsonElement> CaptureAsync(ILocator canvas, string testId)
    {
        await canvas.GetByTestId($"{testId}-capture").ClickAsync();

        return await StoryState.WaitForAsync(
            canvas,
            $"{testId}-state",
            state => state.TryGetProperty("hasCaptured", out var captured) && captured.GetBoolean(),
            $"the {testId} story to finish capturing");
    }

    /// <summary>
    /// Samples one pixel of a story's preview image, at fractional coordinates of its natural size.
    /// Returns [r, g, b, a].
    /// </summary>
    /// <remarks>
    /// This is what turns "the capture succeeded" into "the option reached the pixels": the preview
    /// is drawn onto a scratch canvas and read back, so the assertion is against the encoded image
    /// the library actually produced. Data-URL images are same-origin, so the canvas is not tainted.
    /// </remarks>
    public async Task<int[]> SamplePreviewPixelAsync(string testId, double xFraction, double yFraction)
    {
        return await Page.EvaluateAsync<int[]>(
            @"([id, xf, yf]) => new Promise((resolve, reject) => {
                const img = document.querySelector(`[data-testid=""${id}-preview""]`);
                if (!img) { reject(new Error('no preview image for ' + id)); return; }
                const sample = () => {
                    const canvas = document.createElement('canvas');
                    canvas.width = img.naturalWidth;
                    canvas.height = img.naturalHeight;
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0);
                    const x = Math.min(canvas.width - 1, Math.round(canvas.width * xf));
                    const y = Math.min(canvas.height - 1, Math.round(canvas.height * yf));
                    resolve([...ctx.getImageData(x, y, 1, 1).data]);
                };
                img.complete && img.naturalWidth ? sample() : (img.onload = sample);
            })",
            new object[] { testId, xFraction, yFraction });
    }
}
