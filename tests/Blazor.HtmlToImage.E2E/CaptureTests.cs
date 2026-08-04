using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// Exercises the whole path a consumer depends on: the JS initializer injects the vendored library,
/// the interop module imports, options serialize, and real image bytes come back over the stream.
/// None of that is observable from bUnit, which stubs the interop module out.
/// </summary>
[Collection(DemoCollectionDefinition.Name)]
public sealed class CaptureTests
{
    private readonly DemoFixture _fixture;

    public CaptureTests(DemoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Capture_PngStory_ReturnsBytesWithAPngSignature()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--png");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "png");

        // Assert
        // The signature check is what separates "the promise resolved" from "an actual PNG came
        // back" - a failed capture can still resolve to a tiny, header-less blob.
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected PNG magic bytes. State: {state}");
        Assert.True(StoryState.Int(state, "byteLength") > 1000, $"Expected a non-trivial image. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task Capture_PixelRatioThree_ProducesThreeTimesTheCssWidth()
    {
        // Arrange
        // The poster is pinned to 320 CSS pixels wide in demo.css.
        const int cssWidth = 320;
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--pixel-ratio");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "pixel-ratio");

        // Assert
        // Read from the PNG's own IHDR chunk, so this asserts the encoded image really is
        // higher-resolution rather than merely that an option was passed along.
        Assert.Equal(cssWidth * 3, StoryState.Int(state, "width"));
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task Capture_ExcludeClassAndSelector_EachRemoveTheirOwnElement()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--excluding-elements");

        // Both badges are present in the live DOM - one matches only ExcludeCssClasses, the other
        // only ExcludeSelector, so each one is a separate filter path to prove.
        await canvas.GetByTestId("exclude-badge").WaitForAsync();
        await canvas.GetByTestId("exclude-badge-selector").WaitForAsync();

        // Badge centres as fractions of the poster box. html-to-image sizes the canvas from the
        // node's LAYOUT box and leaves the background where excluded children were - the output
        // never gets shorter. (An earlier version asserted a height shrink and passed only through
        // integer-vs-fractional rounding; the pixels are the real evidence.)
        var badgeFractions = await _fixture.Page.EvaluateAsync<double[][]>(
            @"() => {
                const poster = document.querySelector('[data-testid=""exclude-poster""]').getBoundingClientRect();
                return ['exclude-badge', 'exclude-badge-selector'].map(id => {
                    const badge = document.querySelector(`[data-testid=""${id}""]`).getBoundingClientRect();
                    return [
                        (badge.x - poster.x + badge.width / 2) / poster.width,
                        (badge.y - poster.y + badge.height / 2) / poster.height,
                    ];
                });
            }");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "exclude");

        // Assert
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        // On screen both badge positions are #b4231f red. In the capture the same positions must
        // show the poster's dark background instead - one badge per filter path.
        var filterPaths = new[] { "ExcludeCssClasses", "ExcludeSelector" };
        for (var index = 0; index < badgeFractions.Length; index++)
        {
            var pixel = await _fixture.SamplePreviewPixelAsync("exclude", badgeFractions[index][0], badgeFractions[index][1]);
            Assert.True(
                pixel[0] < 100 && pixel[1] < 100,
                $"{filterPaths[index]} did not exclude its badge: sampled rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}) where the badge sits.");
        }

        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task Capture_JpegStory_ReturnsBytesWithAJpegSignature()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--jpeg");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "jpeg");

        // Assert
        // toBlob honours the `type` option rather than switching entry point, so this also pins that
        // the interop pushes the format into the options for the byte path.
        Assert.True(StoryState.Bool(state, "isJpeg"), $"Expected JPEG magic bytes. State: {state}");
        Assert.False(StoryState.Bool(state, "isPng"), $"Expected NOT a PNG. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task Capture_SvgStory_ReturnsAnSvgDataUrl()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--svg");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "svg");

        // Assert
        Assert.True(StoryState.Bool(state, "isSvgDataUrl"), $"Expected an image/svg+xml data URL. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task Capture_PixelDataStory_ReturnsFourBytesPerPixel()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--pixel-data");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "pixel-data");

        // Assert
        // Raw RGBA, so the length must be divisible by 4. A base64 round-trip that lost or padded
        // bytes would not survive this.
        var byteLength = StoryState.Int(state, "byteLength");
        Assert.True(byteLength > 0, $"Expected pixel data. State: {state}");
        Assert.True(byteLength % 4 == 0, $"Expected RGBA data divisible by 4, got {byteLength}. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task Capture_BackgroundColour_PaintsThePixelsBehindTheRoundedCorners()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--background-colour");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "background");

        // Assert
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        // The poster has rounded corners, so pixel (0,0) lies OUTSIDE the element - without
        // BackgroundColor it is transparent; with #ffd166 it must be that exact colour. This is
        // the difference between "the option was accepted" and "the option reached the pixels".
        var corner = await _fixture.SamplePreviewPixelAsync("background", 0, 0);
        Assert.True(
            Math.Abs(corner[0] - 0xFF) <= 12 && Math.Abs(corner[1] - 0xD1) <= 12 &&
            Math.Abs(corner[2] - 0x66) <= 12 && corner[3] == 255,
            $"Expected corner pixel #ffd166, sampled rgba({corner[0]},{corner[1]},{corner[2]},{corner[3]}).");
        _fixture.AssertNoJsErrors();
    }
}
