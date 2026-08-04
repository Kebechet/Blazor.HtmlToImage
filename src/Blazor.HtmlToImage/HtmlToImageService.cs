using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Kebechet.Blazor.HtmlToImage;

/// <inheritdoc cref="IHtmlToImageService"/>
public sealed class HtmlToImageService : IHtmlToImageService
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
    private bool _isDisposed;

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
        return await module.InvokeAsync<string>("getFontEmbedCss", element, options);
    }

    /// <inheritdoc />
    public async Task<string> GetFontEmbedCssAsync(string elementId, HtmlToImageOptions? options = null)
    {
        var module = await GetModule();
        return await module.InvokeAsync<string>("getFontEmbedCss", elementId, options);
    }

    private async Task<string> CaptureDataUrl(object target, CaptureFormat format, HtmlToImageOptions? options)
    {
        var module = await GetModule();
        return await module.InvokeAsync<string>("toDataUrl", target, format.ToJsName(), options);
    }

    private async Task<byte[]> CaptureBytes(object target, CaptureFormat format, HtmlToImageOptions? options)
    {
        var module = await GetModule();
        await using var streamReference = await module.InvokeAsync<IJSStreamReference>(
            "toStream", target, format.ToJsName(), options);

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
    private async Task<IJSObjectReference> GetModule()
    {
        if (_module is not null)
        {
            return _module;
        }

        await _moduleLock.WaitAsync();
        try
        {
            _module ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", _modulePath);
            return _module;
        }
        finally
        {
            _moduleLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Circuit or WebView already torn down - the JS side is gone with it.
            }

            _module = null;
        }

        _moduleLock.Dispose();
    }
}
