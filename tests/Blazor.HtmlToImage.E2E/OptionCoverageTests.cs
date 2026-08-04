using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// Covers the options and entry points added for full upstream coverage. An option that serializes
/// correctly but never reaches html-to-image produces a capture that looks plausible and ignores the
/// setting, which is exactly what a unit test cannot see.
/// </summary>
[Collection(DemoCollectionDefinition.Name)]
public sealed class OptionCoverageTests
{
    private readonly DemoFixture _fixture;

    public OptionCoverageTests(DemoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ToCanvasAsync_ReturnsALiveCanvasWithRealDimensions()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--canvas");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "canvas");

        // Assert
        // The dimensions are read back off the canvas element over interop, so a non-zero size
        // proves an actual HTMLCanvasElement crossed the boundary rather than a null reference.
        Assert.True(StoryState.Int(state, "width") > 0, $"Expected a real canvas width. State: {state}");
        Assert.True(StoryState.Int(state, "height") > 0, $"Expected a real canvas height. State: {state}");
        // The demo hands the reference back to page JS, which attaches the element - proving the
        // reference revives to the ORIGINAL live canvas, and that it survives disposing the handle.
        var isCanvasAttached = await _fixture.Page.EvaluateAsync<bool>(
            "() => !!document.querySelector('[data-testid=\"canvas-canvas-host\"] canvas')");
        Assert.True(isCanvasAttached, "Expected the live canvas element attached to the page.");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task ImageLoadFailed_ReportsAnImageThatCouldNotResolve()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--broken-images");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "broken");

        // Assert
        // The poster contains one deliberately unresolvable image. Upstream reports it through
        // onImageErrorHandler, which only fires because the demo subscribed to ImageLoadFailed -
        // the wrapper passes no DotNetObjectReference otherwise.
        Assert.True(
            StoryState.Int(state, "failedImages") > 0,
            $"Expected the failed image to be reported. State: {state}");
        // ImagePlaceholder means the capture still succeeds despite the failure.
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG despite the broken image. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task CanvasWidthAndHeight_SizeTheOutputIndependentlyOfTheElement()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--explicit-size");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "size");

        // Assert
        // The story renders the node at 260x160 and exports onto a 520x320 canvas. Reading the
        // dimensions from the PNG's IHDR chunk is what distinguishes "the option was passed" from
        // "the encoded image really is that size".
        Assert.Equal(520, StoryState.Int(state, "width"));
        Assert.Equal(320, StoryState.Int(state, "height"));
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task StyleAndIncludeStyleProperties_CaptureSuccessfully()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--style-overrides");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "style");

        // Assert
        // IncludeStyleProperties restricts which computed properties are copied; naming one that
        // html-to-image cannot resolve throws mid-clone, so a clean PNG is the meaningful signal.
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        Assert.True(StoryState.Int(state, "byteLength") > 500, $"Expected real image data. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task FontAndCacheOptions_CaptureSuccessfully()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--fonts-and-caching");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "fonts");

        // Assert
        // SkipFonts, PreferredFontFormat, CacheBust, IncludeQueryParams, SkipAutoScale and Type all
        // travel together here; any one of them arriving malformed aborts the capture.
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task FetchRequestInit_ReachesTheResourceFetchesWithoutBreakingTheCapture()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--fetch-request-init");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "fetch");

        // Assert
        // A malformed RequestInit makes the browser throw on the first resource fetch rather than
        // being ignored, so a successful capture is what proves the shape survived serialization.
        Assert.True(StoryState.Bool(state, "isPng"), $"Expected a PNG. State: {state}");
        _fixture.AssertNoJsErrors();
    }
}
