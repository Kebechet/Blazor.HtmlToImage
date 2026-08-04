using System.Text.Json.Serialization;

namespace Kebechet.Blazor.HtmlToImage;

/// <summary>
/// Options forwarded to html-to-image. Every member is nullable: leaving one unset is the only
/// signal for "keep html-to-image's own default", so the wrapper never invents a default upstream
/// does not have.
/// </summary>
/// <remarks>
/// Upstream's <c>filter</c> is a JavaScript predicate and cannot cross JS interop as a delegate
/// without a round-trip per DOM node. It is modelled instead as <see cref="ExcludeCssClasses"/>
/// and <see cref="ExcludeSelector"/>, which the interop layer compiles into a single filter
/// function. <c>fetchRequestInit</c> and <c>onImageErrorHandler</c> are not modelled.
/// </remarks>
public sealed class HtmlToImageOptions
{
	/// <summary>Width in pixels applied to the node before rendering.</summary>
	[JsonPropertyName("width")]
	public int? Width { get; set; }

	/// <summary>Height in pixels applied to the node before rendering.</summary>
	[JsonPropertyName("height")]
	public int? Height { get; set; }

	/// <summary>Any valid CSS color used as the background of the captured image.</summary>
	[JsonPropertyName("backgroundColor")]
	public string? BackgroundColor { get; set; }

	/// <summary>Width in pixels applied to the output canvas.</summary>
	[JsonPropertyName("canvasWidth")]
	public int? CanvasWidth { get; set; }

	/// <summary>Height in pixels applied to the output canvas.</summary>
	[JsonPropertyName("canvasHeight")]
	public int? CanvasHeight { get; set; }

	/// <summary>CSS properties copied onto the node's inline style before rendering.</summary>
	[JsonPropertyName("style")]
	public Dictionary<string, string>? Style { get; set; }

	/// <summary>
	/// Restricts style copying to these properties. On a large subtree, naming only what matters is
	/// markedly faster than copying every computed property.
	/// </summary>
	[JsonPropertyName("includeStyleProperties")]
	public string[]? IncludeStyleProperties { get; set; }

	/// <summary>JPEG quality between 0 and 1. Ignored by the PNG and SVG entry points.</summary>
	[JsonPropertyName("quality")]
	public double? Quality { get; set; }

	/// <summary>Appends the current time to requested URLs so caches are bypassed.</summary>
	[JsonPropertyName("cacheBust")]
	public bool? CacheBust { get; set; }

	/// <summary>Uses the whole URL as the cache key instead of stripping its query string.</summary>
	[JsonPropertyName("includeQueryParams")]
	public bool? IncludeQueryParams { get; set; }

	/// <summary>
	/// Data URL substituted for any image that fails to load. Without it a failed image leaves a
	/// blank area, and on some browsers aborts the whole capture.
	/// See <see href="https://github.com/bubkoo/html-to-image/issues/314">html-to-image#314</see>.
	/// </summary>
	[JsonPropertyName("imagePlaceholder")]
	public string? ImagePlaceholder { get; set; }

	/// <summary>
	/// Pixel ratio of the captured image. Defaults to the device's own ratio; set 1 to pin the
	/// output to CSS pixels regardless of display density.
	/// </summary>
	[JsonPropertyName("pixelRatio")]
	public double? PixelRatio { get; set; }

	/// <summary>Skips downloading and embedding web fonts. Much faster when the capture has no custom fonts.</summary>
	[JsonPropertyName("skipFonts")]
	public bool? SkipFonts { get; set; }

	/// <summary>When set, only this font format is embedded and all others are ignored.</summary>
	[JsonPropertyName("preferredFontFormat")]
	public string? PreferredFontFormat { get; set; }

	/// <summary>
	/// Pre-computed font CSS to embed. Pair with <c>GetFontEmbedCssAsync</c> to compute the CSS once
	/// and reuse it across repeated captures instead of re-resolving fonts every time.
	/// </summary>
	[JsonPropertyName("fontEmbedCSS")]
	public string? FontEmbedCss { get; set; }

	/// <summary>Disables the automatic downscaling applied to very large images.</summary>
	[JsonPropertyName("skipAutoScale")]
	public bool? SkipAutoScale { get; set; }

	/// <summary>MIME type of the output image. Defaults to <c>image/png</c>.</summary>
	[JsonPropertyName("type")]
	public string? Type { get; set; }

	/// <summary>
	/// Elements carrying any of these CSS classes are dropped from the capture, along with their
	/// children. Compiled into upstream's <c>filter</c> predicate by the interop layer.
	/// </summary>
	[JsonPropertyName("excludeCssClasses")]
	public string[]? ExcludeCssClasses { get; set; }

	/// <summary>
	/// Elements matching this CSS selector are dropped from the capture, along with their children.
	/// Combined with <see cref="ExcludeCssClasses"/> when both are set.
	/// </summary>
	[JsonPropertyName("excludeSelector")]
	public string? ExcludeSelector { get; set; }
}
