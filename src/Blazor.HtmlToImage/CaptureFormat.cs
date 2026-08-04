namespace Kebechet.Blazor.HtmlToImage;

/// <summary>
/// Which html-to-image entry point a capture should route to.
/// </summary>
internal enum CaptureFormat : byte
{
	Png,
	Jpeg,
	Svg,
	/// <summary>Raw RGBA bytes rather than an encoded image.</summary>
	PixelData
}

internal static class CaptureFormatExtensions
{
	/// <summary>
	/// Maps to the discriminator the interop module switches on. These strings are a wire contract
	/// between <c>HtmlToImageService</c> and <c>html-to-image-interop.js</c> - renaming the enum
	/// member must not silently change them, so they are spelled out rather than derived via
	/// <c>nameof</c>.
	/// </summary>
	internal static string ToJsName(this CaptureFormat format)
	{
		return format switch
		{
			CaptureFormat.Png => "png",
			CaptureFormat.Jpeg => "jpeg",
			CaptureFormat.Svg => "svg",
			CaptureFormat.PixelData => "pixelData",
			_ => throw new NotImplementedException($"Unhandled {nameof(CaptureFormat)} value: {format}")
		};
	}
}
