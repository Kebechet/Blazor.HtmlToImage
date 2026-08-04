using Shouldly;
using Xunit;

namespace Kebechet.Blazor.HtmlToImage.Tests;

public class CaptureFormatTests
{
	[Fact]
	public void ToJsName_EachFormat_MatchesTheInteropDiscriminator()
	{
		// Arrange & Act & Assert
		// These strings are switched on in html-to-image-interop.js; changing one here without
		// changing it there routes the capture to the default branch and throws at runtime.
		CaptureFormat.Png.ToJsName().ShouldBe("png");
		CaptureFormat.Jpeg.ToJsName().ShouldBe("jpeg");
		CaptureFormat.Svg.ToJsName().ShouldBe("svg");
		CaptureFormat.PixelData.ToJsName().ShouldBe("pixelData");
	}

	[Fact]
	public void ToJsName_EveryDeclaredFormat_IsMapped()
	{
		// Arrange
		var formats = Enum.GetValues<CaptureFormat>();

		// Act & Assert
		// Guards the switch against a newly added member falling through to NotImplementedException.
		foreach (var format in formats)
		{
			Should.NotThrow(() => format.ToJsName());
		}
	}

	[Fact]
	public void ToJsName_AllFormats_AreDistinct()
	{
		// Arrange
		var formats = Enum.GetValues<CaptureFormat>();

		// Act
		var names = formats.Select(x => x.ToJsName()).ToList();

		// Assert
		names.Distinct().Count().ShouldBe(names.Count);
	}
}
