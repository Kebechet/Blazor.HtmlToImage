using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Kebechet.Blazor.HtmlToImage;

/// <inheritdoc cref="IHtmlToImageService"/>
public sealed class HtmlToImageService : IHtmlToImageService, IAsyncDisposable
{
    private const string _modulePath = "./_content/Kebechet.Blazor.HtmlToImage/html-to-image-interop.js";

    /// <summary>
    /// Ceiling for a single streamed capture. A capture at a high <c>PixelRatio</c> grows with the
    /// square of the ratio, so the default 32 MB budget is generous for a full-page poster while
    /// still failing loudly instead of exhausting memory on a phone.
    /// </summary>
    private const long _maxStreamBytes = 32L * 1024 * 1024;

    private readonly IJSRuntime _jsRuntime;
    private readonly SemaphoreSlim _moduleLock = new(1, 1);

    private IJSObjectReference? _module;
    private DotNetObjectReference<HtmlToImageService>? _selfRef;
    private bool _isDisposed;

    /// <inheritdoc />
    public event Action<string>? ImageLoadFailed;

    /// <summary>
    /// Invoked from JS for each image that fails to load during a capture. Public because
    /// <c>[JSInvokable]</c> requires it; not part of the interface.
    /// </summary>
    [JSInvokable]
    public void OnImageLoadFailed(string url)
    {
        ImageLoadFailed?.Invoke(url);
    }

    /// <inheritdoc />
    public Task<IJSObjectReference> ToCanvasAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureCanvas(element, options);
    }

    /// <inheritdoc />
    public Task<IJSObjectReference> ToCanvasAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureCanvas(elementId, options);
    }

    private async Task<IJSObjectReference> CaptureCanvas(object target, HtmlToImageOptions? options)
    {
        var module = await GetModule();
        return await module.InvokeAsync<IJSObjectReference>("toCanvas", target, options, ErrorListener);
    }

    /// <summary>
    /// The reference JS reports failed images through, or null when nothing is subscribed - so an
    /// unsubscribed capture creates no <c>DotNetObjectReference</c> and pays no interop cost.
    /// </summary>
    private DotNetObjectReference<HtmlToImageService>? ErrorListener
    {
        get
        {
            if (ImageLoadFailed is null)
            {
                return null;
            }

            return _selfRef ??= DotNetObjectReference.Create(this);
        }
    }

    /// <summary>Creates the service. Prefer <c>AddHtmlToImage()</c> over constructing it directly.</summary>
    public HtmlToImageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc />
    public Task<string> ToPngAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureDataUrl(element, CaptureFormat.Png, options);
    }

    /// <inheritdoc />
    public Task<string> ToPngAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureDataUrl(elementId, CaptureFormat.Png, options);
    }

    /// <inheritdoc />
    public Task<string> ToJpegAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureDataUrl(element, CaptureFormat.Jpeg, options);
    }

    /// <inheritdoc />
    public Task<string> ToJpegAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureDataUrl(elementId, CaptureFormat.Jpeg, options);
    }

    /// <inheritdoc />
    public Task<string> ToSvgAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureDataUrl(element, CaptureFormat.Svg, options);
    }

    /// <inheritdoc />
    public Task<string> ToSvgAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureDataUrl(elementId, CaptureFormat.Svg, options);
    }

    /// <inheritdoc />
    public Task<byte[]> ToPngBytesAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureBytes(element, CaptureFormat.Png, options);
    }

    /// <inheritdoc />
    public Task<byte[]> ToPngBytesAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureBytes(elementId, CaptureFormat.Png, options);
    }

    /// <inheritdoc />
    public Task<byte[]> ToJpegBytesAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureBytes(element, CaptureFormat.Jpeg, options);
    }

    /// <inheritdoc />
    public Task<byte[]> ToJpegBytesAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureBytes(elementId, CaptureFormat.Jpeg, options);
    }

    /// <inheritdoc />
    public Task<byte[]> ToPixelDataAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        return CaptureBytes(element, CaptureFormat.PixelData, options);
    }

    /// <inheritdoc />
    public Task<byte[]> ToPixelDataAsync(string elementId, HtmlToImageOptions? options = null)
    {
        return CaptureBytes(elementId, CaptureFormat.PixelData, options);
    }

    /// <inheritdoc />
    public async Task<string> GetFontEmbedCssAsync(ElementReference element, HtmlToImageOptions? options = null)
    {
        var module = await GetModule();
        return await module.InvokeAsync<string>("getFontEmbedCss", element, options, ErrorListener);
    }

    /// <inheritdoc />
    public async Task<string> GetFontEmbedCssAsync(string elementId, HtmlToImageOptions? options = null)
    {
        var module = await GetModule();
        return await module.InvokeAsync<string>("getFontEmbedCss", elementId, options, ErrorListener);
    }

    private async Task<string> CaptureDataUrl(object target, CaptureFormat format, HtmlToImageOptions? options)
    {
        var module = await GetModule();
        return await module.InvokeAsync<string>("toDataUrl", target, format.ToJsName(), options, ErrorListener);
    }

    private async Task<byte[]> CaptureBytes(object target, CaptureFormat format, HtmlToImageOptions? options)
    {
        var module = await GetModule();
        await using var streamReference = await module.InvokeAsync<IJSStreamReference>(
            "toStream", target, format.ToJsName(), options, ErrorListener);

        await using var stream = await streamReference.OpenReadStreamAsync(_maxStreamBytes);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Imports the interop module once and reuses it. The lock matters because several components
    /// can capture concurrently on first render; without it each would issue its own dynamic
    /// import and the losers' module references would leak.
    /// </summary>
    /// <remarks>
    /// The scope can be torn down while the import is still in flight. The disposal check after the
    /// import is what keeps that late module from outliving the service: <see cref="DisposeAsync"/>
    /// already ran and saw no module to dispose.
    /// </remarks>
    private async Task<IJSObjectReference> GetModule()
    {
        ThrowIfDisposed();
        if (_module is not null)
        {
            return _module;
        }

        await _moduleLock.WaitAsync();
        try
        {
            if (_module is not null)
            {
                return _module;
            }

            var module = await _jsRuntime.InvokeAsync<IJSObjectReference>("import", _modulePath);
            if (_isDisposed)
            {
                await DisposeModule(module);
                ThrowIfDisposed();
            }

            _module = module;
            return _module;
        }
        finally
        {
            _moduleLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(HtmlToImageService));
        }
    }

    private static async Task DisposeModule(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Circuit or WebView already torn down - the JS side is gone with it.
        }
    }

    /// <summary>
    /// Releases the interop module and the callback reference. The DI container calls this when the
    /// scope ends.
    /// </summary>
    /// <remarks>
    /// The module lock is deliberately not disposed: a capture still waiting on the import holds it,
    /// and disposing it under that capture would make its release throw instead of letting it report
    /// the service as disposed.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_module is not null)
        {
            await DisposeModule(_module);
            _module = null;
        }

        _selfRef?.Dispose();
        _selfRef = null;
    }
}
