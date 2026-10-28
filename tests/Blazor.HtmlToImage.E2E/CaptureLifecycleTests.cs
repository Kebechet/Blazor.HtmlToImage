using System.Diagnostics;
using Microsoft.Playwright;
using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// How captures behave over time: bounded when they would otherwise never finish, and kept apart when
/// several run at once on the one scoped service.
/// </summary>
[Collection(DemoCollectionDefinition.Name)]
public sealed class CaptureLifecycleTests
{
    private readonly DemoFixture _fixture;

    public CaptureLifecycleTests(DemoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Capture_ImageRequestThatNeverAnswers_FailsWithTheTimeoutError()
    {
        // Arrange
        // A context of its own: holding a request open would otherwise stall the shared page.
        await using var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        // Neither fulfilled, continued nor aborted, so the request stays open and html-to-image's fetch
        // of the image never settles - only CaptureTimeoutMs (1500 in this story) can end the capture.
        await page.RouteAsync("**/img/never-arrives.png", _ => Task.CompletedTask);
        await page.GotoAsync(
            $"{_fixture.BaseUrl}/iframe.html?viewMode=story&id=capture-htmltoimage--capture-timeout",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => typeof globalThis.htmlToImage !== 'undefined'",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        var canvas = page.Locator("body");
        var stopwatch = Stopwatch.StartNew();

        // Act
        await canvas.GetByTestId("timeout-capture").ClickAsync();
        var state = await StoryState.WaitForAsync(canvas, "timeout-state", StoryState.IsFinished, "the hanging capture to time out");

        // Assert
        Assert.False(StoryState.Bool(state, "hasCaptured"), $"Expected the capture to fail. State: {state}");
        Assert.Contains("did not complete within 1500 ms", state.GetProperty("error").GetString());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8), $"Expected the timeout to end the capture promptly, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Capture_TwoElementsAtOnce_EachReturnsItsOwnElement()
    {
        // Arrange
        var canvas = await _fixture.NavigateToStoryAsync("capture-htmltoimage--concurrent-captures");
        await canvas.GetByTestId("concurrent-narrow-poster").WaitForAsync();
        var cssWidths = await _fixture.Page.EvaluateAsync<int[]>(
            @"() => ['concurrent-poster', 'concurrent-narrow-poster'].map(id =>
                Math.round(document.querySelector(`[data-testid=""${id}""]`).getBoundingClientRect().width))");

        // Act
        var state = await _fixture.CaptureAsync(canvas, "concurrent");

        // Assert
        // The story starts both captures before awaiting either, at pixel ratio 1. Each PNG's IHDR
        // width has to be its own element's width - 320 and 200 - not the other one's.
        Assert.Equal(cssWidths.First(), StoryState.Int(state, "posterWidth"));
        Assert.Equal(cssWidths.Last(), StoryState.Int(state, "narrowPosterWidth"));
        Assert.NotEqual(cssWidths.First(), cssWidths.Last());
        _fixture.AssertNoJsErrors();
    }
}
