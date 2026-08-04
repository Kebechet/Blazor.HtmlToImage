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
    public async Task Capture_ExcludeCssClasses_ShortensTheOutputByTheExcludedElement()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--excluding-elements");

        // The badge is present in the live DOM - only the capture should be missing it.
        await canvas.GetByTestId("exclude-badge").WaitForAsync();
        var posterHeight = await canvas.GetByTestId("exclude-poster").EvaluateAsync<double>(
            "el => el.getBoundingClientRect().height");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "exclude");

        // Assert
        // At PixelRatio 1 the captured height would equal the element's own height if nothing were
        // filtered. The badge is the tallest excluded child, so a correct filter makes the output
        // measurably shorter - which proves the predicate ran, not merely that it was accepted.
        var capturedHeight = StoryState.Int(state, "height");
        Assert.True(
            capturedHeight < posterHeight,
            $"Expected the capture ({capturedHeight}px) to be shorter than the live element ({posterHeight}px) once the badge was excluded. State: {state}");
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
    public async Task Capture_BackgroundColourStory_SucceedsWithoutInteropErrors()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--background-colour");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "background");

        // Assert
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        _fixture.AssertNoJsErrors();
    }
}
