[!["Buy Me A Coffee"](https://www.buymeacoffee.com/assets/img/custom_images/orange_img.png)](https://www.buymeacoffee.com/kebechet)

# Blazor.HtmlToImage
[![NuGet Version](https://img.shields.io/nuget/v/Kebechet.Blazor.HtmlToImage)](https://www.nuget.org/packages/Kebechet.Blazor.HtmlToImage/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Kebechet.Blazor.HtmlToImage)](https://www.nuget.org/packages/Kebechet.Blazor.HtmlToImage/)
[![Build](https://github.com/Kebechet/Blazor.HtmlToImage/actions/workflows/build.yml/badge.svg)](https://github.com/Kebechet/Blazor.HtmlToImage/actions/workflows/build.yml)
[![codecov](https://codecov.io/gh/Kebechet/Blazor.HtmlToImage/graph/badge.svg)](https://codecov.io/gh/Kebechet/Blazor.HtmlToImage)
[![Storybook](https://img.shields.io/badge/storybook-live%20demo-ff4785)](https://kebechet.github.io/Blazor.HtmlToImage/)
![Last updated](https://img.shields.io/github/last-commit/Kebechet/Blazor.HtmlToImage/main?label=last%20updated)
[![Twitter](https://img.shields.io/twitter/url/https/twitter.com/samuel_sidor.svg?style=social&label=Follow%20samuel_sidor)](https://x.com/samuel_sidor)

Blazor wrapper for [html-to-image](https://github.com/bubkoo/html-to-image): capture any DOM element as PNG, JPEG, SVG, raw bytes or pixel data.

The html-to-image build is **vendored and pinned** inside the package and injected by a Blazor JS initializer, so there is no npm step, no CDN, and no `<script>` tag to add. That matters beyond convenience: a CDN reference breaks a MAUI or hybrid app offline, lets a shipped store build change behaviour without a release, and counts as downloading executable code at runtime under App Store guideline 2.5.2.

**[Live storybook](https://kebechet.github.io/Blazor.HtmlToImage/)** - interactive stories for every feature.

## Installation

```bash
dotnet add package Kebechet.Blazor.HtmlToImage
```

Register the service:

```csharp
using Kebechet.Blazor.HtmlToImage;

builder.Services.AddHtmlToImage();
```

## Usage

```razor
@inject IHtmlToImageService _htmlToImage

<div @ref="_poster" class="poster">
    <h1>Bench press - 120 kg x 5</h1>
    <button class="capture-ignore" @onclick="Capture">Share</button>
</div>

@code {
    private ElementReference _poster;

    private async Task Capture()
    {
        var png = await _htmlToImage.ToPngBytesAsync(_poster, new HtmlToImageOptions
        {
            PixelRatio = 3,
            BackgroundColor = "#0b0b0b",
            ExcludeCssClasses = ["capture-ignore"],
        });

        // upload, save, or hand to a native share sheet
    }
}
```

Every method also takes a DOM id instead of an `ElementReference`:

```csharp
var dataUrl = await _htmlToImage.ToPngAsync("poster");
```

### Data URL vs. bytes

`ToPngAsync` returns a `data:image/png;base64,...` string, matching upstream. `ToPngBytesAsync` returns `byte[]` and **streams** the image over JS interop instead of marshalling base64. Prefer the bytes overload for anything you intend to upload or save: a poster at `PixelRatio = 3` is several megabytes, and a data URL carries it as one JSON string - roughly 33% larger and fully buffered on both sides.

### Excluding elements from the capture

Upstream's `filter` is a JavaScript predicate, which cannot cross JS interop as a delegate without a round-trip per DOM node. It is modelled instead as two declarative options that the interop layer compiles into a single filter:

```csharp
new HtmlToImageOptions
{
    ExcludeCssClasses = ["capture-ignore", "debug-overlay"],
    ExcludeSelector = "[data-private]",
}
```

Excluding a node excludes its whole subtree, matching upstream semantics.

### Repeated captures of the same subtree

Font resolution dominates the cost of a capture that uses web fonts. Resolve once and reuse:

```csharp
var fontCss = await _htmlToImage.GetFontEmbedCssAsync(_poster);

foreach (var frame in frames)
{
    var png = await _htmlToImage.ToPngBytesAsync(_poster, new HtmlToImageOptions
    {
        FontEmbedCss = fontCss,
    });
}
```

### iOS/WebKit blank images and hangs - handled by default

Two Safari/WebKit failure modes are mitigated in the interop layer, on by default:

- **Blank large images.** WebKit rasterises the capture's intermediate SVG before large embedded
  images have finished decoding, so on iOS the first capture of an image-heavy subtree comes back
  with those images blank - and a plain retry is not enough, because a single re-capture can stay
  blank too. Measured on a real iPhone: repeated captures converge on the complete image by the
  second or third attempt, while pre-decoding the embedded images does not help (WebKit's SVG-image
  loader does not share the page's decode cache). Captures therefore repeat until two consecutive
  results are identical, capped by `StabilizeAttempts` (default 3, set 1 to opt out). A stabilised
  capture of a heavy subtree costs roughly one extra attempt (~2x a single capture) on the first
  call.
- **Captures that never return.** Safari rejects `HTMLImageElement.decode()` under memory pressure
  even for successfully loaded images, and upstream's `createImage` has no rejection path - the
  capture promise then never settles. Every capture is bounded by `CaptureTimeoutMs` (default
  30000) and fails with a descriptive error instead of hanging forever.

Subtrees with genuinely dynamic content (a running clock, an animation) never produce two identical
captures; they take `StabilizeAttempts` captures and return the last one. Set `StabilizeAttempts = 1`
for those.

Both mitigations are also proposed upstream: the never-settling capture as
https://github.com/bubkoo/html-to-image/pull/589 and the stabilisation loop as an opt-in
`stabilizationAttempts` option in https://github.com/bubkoo/html-to-image/pull/591. Once a vendored
release contains 589, the timeout stays as a plain safety bound; once it contains 591, the interop's
`captureStable` loop can be replaced by forwarding `StabilizeAttempts` to upstream's option.

## Coverage vs. html-to-image 1.11.13

**Complete.** Every upstream entry point and every option is reachable.

| Axis | html-to-image | This package |
|---|---:|---:|
| Entry points | 7 | 7 |
| Options | 20 | 20 |

**Entry points:** `toPng`, `toJpeg`, `toSvg`, `toPixelData`, `getFontEmbedCSS` and `toCanvas` map to `ToPngAsync`, `ToJpegAsync`, `ToSvgAsync`, `ToPixelDataAsync`, `GetFontEmbedCssAsync` and `ToCanvasAsync`. `toBlob`'s role is served by `ToPngBytesAsync` / `ToJpegBytesAsync`, which route through `toCanvas` to work around an upstream bug - see the deviation note below.

**Options:** all 20, though three are reshaped because their upstream form is a JavaScript value that cannot cross interop as data:

| Upstream | Here | Why |
|---|---|---|
| `filter` | `ExcludeCssClasses`, `ExcludeSelector` | A JS predicate would need an interop round-trip per DOM node; these compile into one filter function |
| `onImageErrorHandler` | `ImageLoadFailed` event | A JS callback cannot be an option value; wired through a `DotNetObjectReference` only while subscribed |
| `fetchRequestInit` | `FetchRequestInit` class | Models the data-carrying members; `signal`, `body` and `window` are live JS objects with no data form |

⚠️ **`ImageLoadFailed` and `ImagePlaceholder` are alternatives, not complements.** Upstream resolves a failed URL to the placeholder *before* assigning it to the cloned image, so with a placeholder set the clone loads successfully and the error never fires. Pick reporting or papering over. Pinned by `ImageLoadFailed_ReportsAnImageThatCouldNotResolve`.

Coverage is measured against html-to-image's published type definitions - `lib/index.d.ts` for entry points, `lib/types.d.ts` for the `Options` interface - excluding underscore-prefixed internals. Re-checked on every upstream version bump.

### One deliberate deviation from upstream

⚠️ **`ToJpegBytesAsync` does not call upstream's `toBlob`.**

Upstream's `toBlob` forwards its options to `toCanvas` but then calls its own internal `canvasToBlob(canvas)` with **no options at all**, so `type` and `quality` are silently dropped - asking it for a JPEG returns a PNG at quality 1. This wrapper calls `toCanvas` and does the canvas-to-blob step itself, which is what makes `ToJpegBytesAsync` return real JPEG bytes and `Quality` take effect. Pinned by the `Capture_JpegStory_ReturnsBytesWithAJpegSignature` browser test.

Upstreamed as https://github.com/bubkoo/html-to-image/pull/590 - once that ships in a vendored release, the manual canvas-to-blob step in `toStream` can be replaced with upstream `toBlob` (keep the browser test either way).

## Captures stall in hidden tabs - by upstream design

⚠️ **A capture started while the tab is hidden or fully occluded does not complete until the tab renders a frame again.** Nothing errors and nothing times out - the Task just stays pending, then resolves when the tab becomes visible.

This is upstream's own resolution path, not wrapper behaviour: html-to-image's `createImage` resolves every capture inside `img.decode().then(() => requestAnimationFrame(resolve))`, and browsers do not fire `requestAnimationFrame` for hidden documents. The normal case - capturing in response to a user action in a visible tab - never sees this. It matters only if a capture races a tab switch, or if you drive the page from automation that keeps it backgrounded (CDP screenshots force a frame, which un-stalls it).

## Vendored library provenance

| | |
|---|---|
| Version | html-to-image 1.11.13 |
| File | `wwwroot/html-to-image.js` |
| Source | npm tarball `html-to-image-1.11.13.tgz`, `package/dist/html-to-image.js` |
| SHA-256 | `a90b42909d80964269ef6d5f3d1e4a5a7e2a4c263a5d2a76a9e7151901343262` |

The npm `dist` build is already minified - jsDelivr reports "skipped minification" for it - so it is vendored verbatim rather than re-minified, and the hash above verifies byte-for-byte against the tarball.

## License

[MIT](LICENSE). html-to-image is itself [MIT licensed](https://github.com/bubkoo/html-to-image/blob/master/LICENSE).
