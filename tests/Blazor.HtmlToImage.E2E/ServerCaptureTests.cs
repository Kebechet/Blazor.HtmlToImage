using Xunit;

namespace Blazor.HtmlToImage.E2E;

/// <summary>
/// Captures over a Blazor Server circuit. A JS-to-.NET message larger than SignalR's 32 KB limit does
/// not fail the one call - it closes the whole circuit, and the page stops responding.
/// </summary>
[Collection(ServerHostCollectionDefinition.Name)]
public sealed class ServerCaptureTests
{
    private const int SignalRMessageLimitBytes = 32 * 1024;

    /// <summary>MAX_REPORTED_IMAGE_SOURCE_LENGTH in html-to-image-interop.js.</summary>
    private const int ReportedImageSourceLimit = 2048;

    private readonly ServerHostFixture _fixture;

    public ServerCaptureTests(ServerHostFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("svg")]
    public async Task DataUrlCapture_LargerThanOneSignalRMessage_ArrivesOverTheCircuit(string format)
    {
        // Arrange
        var page = await _fixture.OpenCapturePageAsync();

        // Act
        await page.GetByTestId(format).ClickAsync();
        var state = await StoryState.WaitForAsync(page, "state", StoryState.IsFinished, $"the {format} data URL to arrive");

        // Assert
        Assert.True(StoryState.Bool(state, "hasCaptured"), $"Expected the {format} capture to succeed. State: {state}{Environment.NewLine}{_fixture.ServerLog()}");
        Assert.True(StoryState.Bool(state, "hasExpectedPrefix"), $"Expected a {format} data URL. State: {state}");
        // Pins that the grid really is larger than one message, so this test exercises the limit.
        Assert.True(StoryState.Int(state, "length") > SignalRMessageLimitBytes, $"Expected a data URL above {SignalRMessageLimitBytes} chars. State: {state}");
        _fixture.AssertNoJsErrors();
    }

    [Fact]
    public async Task ImageLoadFailed_UndecodableImageLargerThanOneSignalRMessage_IsReportedOverTheCircuit()
    {
        // Arrange
        var page = await _fixture.OpenCapturePageAsync();

        // Act
        await page.GetByTestId("undecodable").ClickAsync();
        var state = await StoryState.WaitForAsync(page, "state", StoryState.IsFinished, "the capture with an undecodable image to finish");

        // Assert
        // The failed image arrives as a data URL of its 40 KB of bytes. Sent whole, that one event
        // disconnects the circuit before the capture can report back.
        Assert.True(StoryState.Bool(state, "hasCaptured"), $"Expected the capture to succeed. State: {state}{Environment.NewLine}{_fixture.ServerLog()}");
        Assert.True(StoryState.Int(state, "failedImageCount") > 0, $"Expected the undecodable image to be reported. State: {state}");
        Assert.InRange(StoryState.Int(state, "longestFailedImageLength"), 1, ReportedImageSourceLimit);
        _fixture.AssertNoJsErrors();
    }
}
