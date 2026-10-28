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

    private void StubImport(ValueTask<IJSObjectReference> import)
    {
        _jsRuntime.InvokeAsync<IJSObjectReference>(ImportIdentifier, Arg.Any<object?[]?>()).Returns(import);
        _jsRuntime.InvokeAsync<IJSObjectReference>(ImportIdentifier, Arg.Any<CancellationToken>(), Arg.Any<object?[]?>()).Returns(import);
    }
}
