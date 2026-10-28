using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kebechet.Blazor.HtmlToImage.Tests;

public class HtmlToImageServiceTests
{
    private const string ImportIdentifier = "import";

    private readonly IJSRuntime _jsRuntime = Substitute.For<IJSRuntime>();

    [Fact]
    public void ServiceInterface_AsDeclared_IsNotAsyncDisposable()
    {
        // Arrange
        var serviceInterface = typeof(IHtmlToImageService);

        // Act
        var isAsyncDisposable = typeof(IAsyncDisposable).IsAssignableFrom(serviceInterface);

        // Assert
        // The service is one scoped instance shared by every component in the scope. DisposeAsync on
        // the interface invites a single component to dispose it for all the others; the DI container
        // owns its lifetime and disposes the implementation regardless of the interface.
        isAsyncDisposable.ShouldBeFalse();
    }

    [Fact]
    public async Task ScopeDisposal_AfterResolvingTheService_FailsLaterCapturesAsDisposed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(_jsRuntime);
        services.AddHtmlToImage();
        await using var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateAsyncScope();
        var htmlToImageService = scope.ServiceProvider.GetRequiredService<IHtmlToImageService>();

        // Act
        await scope.DisposeAsync();

        // Assert
        var exception = await Should.ThrowAsync<ObjectDisposedException>(() => htmlToImageService.ToPngAsync("poster"));
        exception.ObjectName.ShouldBe(nameof(HtmlToImageService));
    }

    [Fact]
    public async Task DisposeAsync_WhileTheModuleImportIsPending_DisposesTheModuleOnceItArrives()
    {
        // Arrange
        var module = Substitute.For<IJSObjectReference>();
        var pendingImport = new TaskCompletionSource<IJSObjectReference>();
        StubImport(new ValueTask<IJSObjectReference>(pendingImport.Task));
        var htmlToImageService = new HtmlToImageService(_jsRuntime);
        var capture = htmlToImageService.ToPngAsync("poster");

        // Act
        await htmlToImageService.DisposeAsync();
        pendingImport.SetResult(module);

        // Assert
        // The import resolves after teardown, so nothing will ever dispose it unless the import path
        // notices the service is gone.
        var exception = await Should.ThrowAsync<ObjectDisposedException>(() => capture);
        exception.ObjectName.ShouldBe(nameof(HtmlToImageService));
        await module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task ToPngBytesAsync_MaxResultBytesSet_CapsTheStreamAtThatSize()
    {
        // Arrange
        const long maxResultBytes = 64L * 1024 * 1024;
        var streamReference = StubCapturedStream();
        var htmlToImageService = new HtmlToImageService(_jsRuntime);
        var options = new HtmlToImageOptions { MaxResultBytes = maxResultBytes };

        // Act
        await htmlToImageService.ToPngBytesAsync("poster", options);

        // Assert
        await streamReference.Received(1).OpenReadStreamAsync(maxResultBytes, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ToPngBytesAsync_MaxResultBytesUnset_CapsTheStreamAt32Megabytes()
    {
        // Arrange
        const long defaultMaxResultBytes = 32L * 1024 * 1024;
        var streamReference = StubCapturedStream();
        var htmlToImageService = new HtmlToImageService(_jsRuntime);

        // Act
        await htmlToImageService.ToPngBytesAsync("poster");

        // Assert
        await streamReference.Received(1).OpenReadStreamAsync(defaultMaxResultBytes, Arg.Any<CancellationToken>());
    }

    public static TheoryData<string> TextCaptures()
    {
        return new TheoryData<string>
        {
            nameof(IHtmlToImageService.ToPngAsync),
            nameof(IHtmlToImageService.ToJpegAsync),
            nameof(IHtmlToImageService.ToSvgAsync),
            nameof(IHtmlToImageService.GetFontEmbedCssAsync),
        };
    }

    [Theory]
    [MemberData(nameof(TextCaptures))]
    public async Task TextCapture_AnyResult_IsReadFromAStreamRatherThanOneInteropMessage(string captureName)
    {
        // Arrange
        // On Blazor Server a JS result crosses SignalR as one message capped at 32 KB, and a larger one
        // closes the circuit. Data URLs and embedded font CSS are routinely megabytes, so they have to
        // come back the way the byte captures do - as a stream.
        const string dataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        StubCapturedStream(Encoding.UTF8.GetBytes(dataUrl));
        var htmlToImageService = new HtmlToImageService(_jsRuntime);

        // Act
        var result = await (Task<string>)Capture(htmlToImageService, captureName, false, CancellationToken.None);

        // Assert
        result.ShouldBe(dataUrl);
    }

    [Theory]
    [MemberData(nameof(TextCaptures))]
    public async Task TextCapture_ResultBehindAByteOrderMark_ReturnsTheTextWithoutTheMark(string captureName)
    {
        // Arrange
        // The interop prefixes every text result with a UTF-8 byte-order mark: Blazor cannot stream
        // zero bytes, and an empty string is a valid answer - font CSS for a page with no web fonts.
        byte[] byteOrderMark = [0xEF, 0xBB, 0xBF];
        StubCapturedStream(byteOrderMark);
        var htmlToImageService = new HtmlToImageService(_jsRuntime);

        // Act
        var result = await (Task<string>)Capture(htmlToImageService, captureName, false, CancellationToken.None);

        // Assert
        result.ShouldBeEmpty();
    }

    public static TheoryData<string, bool> Captures()
    {
        var captures = new TheoryData<string, bool>();
        string[] captureNames =
        [
            nameof(IHtmlToImageService.ToPngAsync),
            nameof(IHtmlToImageService.ToJpegAsync),
            nameof(IHtmlToImageService.ToSvgAsync),
            nameof(IHtmlToImageService.ToPngBytesAsync),
            nameof(IHtmlToImageService.ToJpegBytesAsync),
            nameof(IHtmlToImageService.ToPixelDataAsync),
            nameof(IHtmlToImageService.GetFontEmbedCssAsync),
            nameof(IHtmlToImageService.ToCanvasAsync),
        ];
        foreach (var captureName in captureNames)
        {
            captures.Add(captureName, true);
            captures.Add(captureName, false);
        }

        return captures;
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public async Task Capture_CancelledWhileTheBrowserIsCapturing_StopsWaiting(string captureName, bool isByElementReference)
    {
        // Arrange
        var module = Substitute.For<IJSObjectReference>();
        StubImport(new ValueTask<IJSObjectReference>(module));
        StubPendingUntilCancelled<string>(module);
        StubPendingUntilCancelled<IJSStreamReference>(module);
        StubPendingUntilCancelled<IJSObjectReference>(module);
        var htmlToImageService = new HtmlToImageService(_jsRuntime);
        using var cancellation = new CancellationTokenSource();
        var capture = Capture(htmlToImageService, captureName, isByElementReference, cancellation.Token);

        // Act
        await cancellation.CancelAsync();

        // Assert
        // html-to-image has no abort, so the browser finishes the capture regardless - cancellation is
        // about the caller no longer waiting for it. The bound turns "never honoured" into a failure
        // instead of a hung test run.
        await Should.ThrowAsync<OperationCanceledException>(() => capture.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static Task Capture(IHtmlToImageService htmlToImageService, string captureName, bool isByElementReference, CancellationToken cancellationToken)
    {
        const string elementId = "poster";
        var element = new ElementReference(elementId);
        return (captureName, isByElementReference) switch
        {
            (nameof(IHtmlToImageService.ToPngAsync), true) => htmlToImageService.ToPngAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToPngAsync), false) => htmlToImageService.ToPngAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToJpegAsync), true) => htmlToImageService.ToJpegAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToJpegAsync), false) => htmlToImageService.ToJpegAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToSvgAsync), true) => htmlToImageService.ToSvgAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToSvgAsync), false) => htmlToImageService.ToSvgAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToPngBytesAsync), true) => htmlToImageService.ToPngBytesAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToPngBytesAsync), false) => htmlToImageService.ToPngBytesAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToJpegBytesAsync), true) => htmlToImageService.ToJpegBytesAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToJpegBytesAsync), false) => htmlToImageService.ToJpegBytesAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToPixelDataAsync), true) => htmlToImageService.ToPixelDataAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToPixelDataAsync), false) => htmlToImageService.ToPixelDataAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.GetFontEmbedCssAsync), true) => htmlToImageService.GetFontEmbedCssAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.GetFontEmbedCssAsync), false) => htmlToImageService.GetFontEmbedCssAsync(elementId, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToCanvasAsync), true) => htmlToImageService.ToCanvasAsync(element, cancellationToken: cancellationToken),
            (nameof(IHtmlToImageService.ToCanvasAsync), false) => htmlToImageService.ToCanvasAsync(elementId, cancellationToken: cancellationToken),
            _ => throw new NotImplementedException($"Unhandled capture {captureName}")
        };
    }

    /// <summary>
    /// Every interop call stays pending until its token is cancelled, as Blazor's JS runtime behaves
    /// for a JS promise that has not settled. The overloads without a token never complete, so a call
    /// that drops the token waits forever.
    /// </summary>
    private static void StubPendingUntilCancelled<TValue>(IJSObjectReference module)
    {
        module.InvokeAsync<TValue>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
            .Returns(callInfo => new ValueTask<TValue>(PendingUntilCancelled<TValue>(callInfo.ArgAt<CancellationToken>(1))));
        module.InvokeAsync<TValue>(Arg.Any<string>(), Arg.Any<object?[]?>())
            .Returns(new ValueTask<TValue>(new TaskCompletionSource<TValue>().Task));
    }

    private static Task<TValue> PendingUntilCancelled<TValue>(CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource<TValue>();
        cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
        return pending.Task;
    }

    /// <summary>
    /// Imports a module whose captures return a stream reference over <paramref name="content"/> -
    /// three arbitrary bytes by default - and returns that reference so the test can inspect how it
    /// was read.
    /// </summary>
    private IJSStreamReference StubCapturedStream(byte[]? content = null)
    {
        var module = Substitute.For<IJSObjectReference>();
        StubImport(new ValueTask<IJSObjectReference>(module));
        var streamReference = Substitute.For<IJSStreamReference>();
        streamReference.OpenReadStreamAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<Stream>(new MemoryStream(content ?? [1, 2, 3])));
        module.InvokeAsync<IJSStreamReference>(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
            .Returns(new ValueTask<IJSStreamReference>(streamReference));
        return streamReference;
    }

    private void StubImport(ValueTask<IJSObjectReference> import)
    {
        _jsRuntime.InvokeAsync<IJSObjectReference>(ImportIdentifier, Arg.Any<object?[]?>()).Returns(import);
        _jsRuntime.InvokeAsync<IJSObjectReference>(ImportIdentifier, Arg.Any<CancellationToken>(), Arg.Any<object?[]?>()).Returns(import);
    }
}
