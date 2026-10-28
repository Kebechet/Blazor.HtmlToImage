using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace Kebechet.Blazor.HtmlToImage;

/// <summary>
/// Captures a live DOM element as an image, via html-to-image.
/// </summary>
/// <remarks>
/// Every method has an <see cref="ElementReference"/> overload and a DOM-id overload. Prefer the
/// <see cref="ElementReference"/> one: it is checked by the compiler and survives an id rename,
/// whereas an id is resolved at call time and throws when nothing matches.
/// <para>
/// The <c>*BytesAsync</c> methods stream the image over JS interop rather than marshalling a
/// base64 data URL. A poster captured at <c>PixelRatio = 3</c> is several megabytes, and a data URL
/// carries it as a single JSON string - roughly 33% larger and fully buffered on both sides.
/// </para>
/// <para>
/// Cancelling the token stops the caller waiting; it does not stop the browser. html-to-image has
/// no abort, so a cancelled capture still runs to completion (or to <c>CaptureTimeoutMs</c>) in
/// the page and its result is discarded.
/// </para>
/// <para>
/// Deliberately not <see cref="IAsyncDisposable"/>: the instance is scoped and shared by every
/// component in the scope, so its lifetime belongs to the DI container, which disposes the
/// implementation when the scope ends.
/// </para>
/// </remarks>
public interface IHtmlToImageService
{
    /// <summary>Captures the element as a PNG data URL (<c>data:image/png;base64,...</c>).</summary>
    Task<string> ToPngAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToPngAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<string> ToPngAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Captures the element as a JPEG data URL. Set <see cref="HtmlToImageOptions.Quality"/> to control compression.</summary>
    Task<string> ToJpegAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToJpegAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<string> ToJpegAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Captures the element as an SVG data URL with the subtree inlined as a foreignObject.</summary>
    Task<string> ToSvgAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToSvgAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<string> ToSvgAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Captures the element as raw PNG bytes, streamed rather than base64-marshalled.</summary>
    Task<byte[]> ToPngBytesAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToPngBytesAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<byte[]> ToPngBytesAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Captures the element as raw JPEG bytes, streamed rather than base64-marshalled.</summary>
    Task<byte[]> ToJpegBytesAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToJpegBytesAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<byte[]> ToJpegBytesAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the element's raw RGBA pixels with the canvas dimensions they cover - the element's
    /// size times the pixel ratio.
    /// </summary>
    /// <remarks>
    /// Does not call upstream's <c>toPixelData</c>, which reads the element's CSS size from a canvas
    /// that is pixel-ratio times larger and so returns only its top-left corner on any high-density
    /// screen.
    /// </remarks>
    Task<PixelData> ToPixelDataAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToPixelDataAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<PixelData> ToPixelDataAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the element's web fonts to embeddable CSS. Compute this once and pass it back via
    /// <see cref="HtmlToImageOptions.FontEmbedCss"/> when capturing the same subtree repeatedly -
    /// font resolution is the dominant cost of a capture that uses custom fonts.
    /// </summary>
    Task<string> GetFontEmbedCssAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="GetFontEmbedCssAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<string> GetFontEmbedCssAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures the element onto a live <c>HTMLCanvasElement</c> and returns a reference to it,
    /// for handing to other JavaScript - a WebGL texture, a custom encoder, a compositing step.
    /// </summary>
    /// <remarks>
    /// The returned reference owns a real DOM element; dispose it when you are done, or the canvas
    /// and its backing bitmap stay alive for the lifetime of the circuit.
    /// </remarks>
    Task<IJSObjectReference> ToCanvasAsync(ElementReference element, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ToCanvasAsync(ElementReference, HtmlToImageOptions?, CancellationToken)"/>
    Task<IJSObjectReference> ToCanvasAsync(string elementId, HtmlToImageOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised for every image that fails to load during a capture, carrying the URL that failed.
    /// </summary>
    /// <remarks>
    /// Upstream exposes this as the <c>onImageErrorHandler</c> option - a JavaScript callback, which
    /// cannot be modelled as an option value. It is surfaced as an event instead, wired through a
    /// <c>DotNetObjectReference</c> only while something is subscribed, so an unsubscribed capture
    /// pays no interop cost. Pair it with <see cref="HtmlToImageOptions.ImagePlaceholder"/> to both
    /// see and paper over the failure.
    /// </remarks>
    event Action<string>? ImageLoadFailed;
}
