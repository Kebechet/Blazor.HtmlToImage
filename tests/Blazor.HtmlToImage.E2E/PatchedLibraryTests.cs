using Microsoft.Playwright;
using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// Behaviour that only exists because the vendored bundle carries a fix html-to-image has not
/// released yet. See the README section "Patched html-to-image".
/// </summary>
[Collection(DemoCollectionDefinition.Name)]
public sealed class PatchedLibraryTests
{
    private const string FetchedImageGlob = "**/img/fetched-square.svg";

    private readonly DemoFixture _fixture;

    public PatchedLibraryTests(DemoFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// https://github.com/bubkoo/html-to-image/pull/602: .NET MAUI's BlazorWebView on Android sends
    /// Content-Type twice for every static file, so html-to-image 1.11.13 embedded the app's own
    /// images as <c>data:image/svg+xml;base64, image/svg+xml;base64</c> and every capture containing
    /// one failed with a bare <c>[object Event]</c>.
    /// </summary>
    /// <remarks>
    /// Playwright keeps only the last of two headers with the same name, so the response carries the
    /// value fetch() reports for two Content-Type lines: both joined with a comma. Only Chromium keeps
    /// that raw value as the blob type; WebKit parses it to one media type, so on WebKit this passes
    /// without the patch too.
    /// </remarks>
    [Fact]
    public async Task Capture_ImageServedWithARepeatedContentType_EmbedsTheImage()
    {
        // Arrange
        await _fixture.Page.RouteAsync(FetchedImageGlob, async route =>
        {
            var response = await route.FetchAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = response.Status,
                BodyBytes = await response.BodyAsync(),
                Headers = [new KeyValuePair<string, string>("Content-Type", "image/svg+xml, image/svg+xml")],
            });
        });

        try
        {
            var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--fetch-request-init");
            await canvas.GetByTestId("fetch-fetched-image").WaitForAsync();
            // What the page's fetch() sees is the precondition: two header lines joined into one value.
            var contentTypeSeenByFetch = await _fixture.Page.EvaluateAsync<string>(
                "async () => (await fetch('img/fetched-square.svg', { cache: 'no-store' })).headers.get('Content-Type')");
            var imageFraction = await _fixture.Page.EvaluateAsync<double[]>(
                @"() => {
					const poster = document.querySelector('[data-testid=""fetch-poster""]').getBoundingClientRect();
					const image = document.querySelector('[data-testid=""fetch-fetched-image""]').getBoundingClientRect();
					return [
						(image.x - poster.x + image.width / 2) / poster.width,
						(image.y - poster.y + image.height / 2) / poster.height,
					];
				}");

            // Act
            var state = await _fixture.CaptureAsync(canvas, "fetch");

            // Assert
            Assert.Equal("image/svg+xml, image/svg+xml", contentTypeSeenByFetch);
            Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
            var pixel = await _fixture.SamplePreviewPixelAsync("fetch", imageFraction[0], imageFraction[1]);
            Assert.True(
                Math.Abs(pixel[0] - 0x2a) <= 12 && Math.Abs(pixel[1] - 0x9d) <= 12 && Math.Abs(pixel[2] - 0x8f) <= 12,
                $"Expected the fetched #2a9d8f square in the capture, sampled rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}).");
            _fixture.AssertNoJsErrors();
        }
        finally
        {
            await _fixture.Page.UnrouteAsync(FetchedImageGlob);
        }
    }
}
