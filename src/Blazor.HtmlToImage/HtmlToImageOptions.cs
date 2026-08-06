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

    /// <summary>
    /// Request options applied to every resource html-to-image fetches while inlining the subtree -
    /// images, fonts, stylesheets. Set it when those resources need credentials or a specific CORS
    /// mode, which is the usual reason an otherwise-correct capture comes back with blank images.
    /// </summary>
    [JsonPropertyName("fetchRequestInit")]
    public FetchRequestInit? FetchRequestInit { get; set; }

    /// <summary>
    /// Maximum captures taken until two consecutive results are identical; the last result is
    /// returned. Defaults to 3. Set 1 to take a single capture with no stabilisation.
    /// </summary>
    /// <remarks>
    /// WebKit rasterises the capture's intermediate SVG before large embedded images have decoded,
    /// so on iOS/Safari the first capture of an image-heavy subtree comes back with those images
    /// blank - and a plain retry is not enough, because the decoded output stays wrong until the
    /// engine settles. Measured on a real iPhone: repeated captures converge on the complete image
    /// by the second or third attempt, while single captures stay blank indefinitely; pre-decoding
    /// the embedded images does not help because WebKit's SVG-image loader does not share the
    /// page's decode cache. Handled by the interop layer, never forwarded to html-to-image.
    /// Upstreamed as https://github.com/bubkoo/html-to-image/pull/591.
    /// </remarks>
    [JsonPropertyName("stabilizeAttempts")]
    public int? StabilizeAttempts { get; set; }

    /// <summary>
    /// Upper bound in milliseconds for a single capture attempt before it fails with a descriptive
    /// error. Defaults to 30000.
    /// </summary>
    /// <remarks>
    /// html-to-image's internal image loading can hang forever: Safari rejects
    /// <c>HTMLImageElement.decode()</c> under memory pressure and upstream's <c>createImage</c> has
    /// no rejection path, leaving the capture promise permanently unsettled. The bound turns that
    /// hang into an error the caller can see. Handled by the interop layer, never forwarded to
    /// html-to-image. The upstream hang itself is fixed by
    /// https://github.com/bubkoo/html-to-image/pull/589.
    /// </remarks>
    [JsonPropertyName("captureTimeoutMs")]
    public int? CaptureTimeoutMs { get; set; }
}

/// <summary>
/// The subset of the browser's <c>RequestInit</c> that is meaningful for resource fetches during a
/// capture and can cross JS interop as data.
/// </summary>
/// <remarks>
/// <c>RequestInit</c> also carries members that are live JavaScript objects - <c>signal</c>,
/// <c>body</c>, <c>window</c> - which have no data representation. They are omitted rather than
/// half-modelled; the fields below are the ones that change whether a resource loads at all.
/// </remarks>
public sealed class FetchRequestInit
{
    /// <summary>CORS mode, e.g. <c>cors</c>, <c>no-cors</c>, <c>same-origin</c>.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>Whether cookies travel with the request: <c>omit</c>, <c>same-origin</c>, <c>include</c>.</summary>
    [JsonPropertyName("credentials")]
    public string? Credentials { get; set; }

    /// <summary>Cache mode, e.g. <c>default</c>, <c>no-store</c>, <c>reload</c>.</summary>
    [JsonPropertyName("cache")]
    public string? Cache { get; set; }

    /// <summary>Redirect handling: <c>follow</c>, <c>error</c>, <c>manual</c>.</summary>
    [JsonPropertyName("redirect")]
    public string? Redirect { get; set; }

    /// <summary>Referrer URL sent with the request.</summary>
    [JsonPropertyName("referrer")]
    public string? Referrer { get; set; }

    /// <summary>Referrer policy, e.g. <c>no-referrer</c>, <c>origin</c>.</summary>
    [JsonPropertyName("referrerPolicy")]
    public string? ReferrerPolicy { get; set; }

    /// <summary>Subresource-integrity metadata for the fetched resource.</summary>
    [JsonPropertyName("integrity")]
    public string? Integrity { get; set; }

    /// <summary>Additional request headers.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}
