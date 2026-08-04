using Xunit;
using System.Text.Json;
using Shouldly;

namespace Kebechet.Blazor.HtmlToImage.Tests;

/// <summary>
/// The JSON property names are a wire contract with html-to-image's own option object. A rename on
/// the C# side that is not mirrored in <c>[JsonPropertyName]</c> silently stops the option reaching
/// the library - the capture still succeeds, just ignoring the setting.
/// </summary>
public class HtmlToImageOptionsTests
{
	[Fact]
	public void Serialize_OptionsWithUpstreamNames_UsesExactUpstreamCasing()
	{
		// Arrange
		var options = new HtmlToImageOptions
		{
			BackgroundColor = "#fff",
			CanvasWidth = 100,
			PixelRatio = 3,
			CacheBust = true,
			IncludeQueryParams = true,
			ImagePlaceholder = "data:image/png;base64,AAA",
			SkipAutoScale = true,
			SkipFonts = false,
			PreferredFontFormat = "woff2",
			FontEmbedCss = ".a{}",
			IncludeStyleProperties = ["color"]
		};

		// Act
		var json = JsonSerializer.Serialize(options);

		// Assert
		json.ShouldContain("\"backgroundColor\"");
		json.ShouldContain("\"canvasWidth\"");
		json.ShouldContain("\"pixelRatio\"");
		json.ShouldContain("\"cacheBust\"");
		json.ShouldContain("\"includeQueryParams\"");
		json.ShouldContain("\"imagePlaceholder\"");
		json.ShouldContain("\"skipAutoScale\"");
		json.ShouldContain("\"skipFonts\"");
		json.ShouldContain("\"preferredFontFormat\"");
		// Upstream spells this one with a capitalised acronym; camelCase would be silently ignored.
		json.ShouldContain("\"fontEmbedCSS\"");
		json.ShouldContain("\"includeStyleProperties\"");
	}

	[Fact]
	public void Serialize_DefaultOptions_LeavesEveryMemberNull()
	{
		// Arrange
		var options = new HtmlToImageOptions();

		// Act
		var json = JsonSerializer.Serialize(options);

		// Assert
		// Every value must be null so the interop layer can drop it and let html-to-image apply its
		// own default. A non-null default here would silently override upstream behaviour.
		using var document = JsonDocument.Parse(json);
		foreach (var property in document.RootElement.EnumerateObject())
		{
			property.Value.ValueKind.ShouldBe(JsonValueKind.Null, $"{property.Name} should be unset by default");
		}
	}

	[Fact]
	public void Serialize_ExcludeMembers_AreCarriedForTheInteropFilter()
	{
		// Arrange
		var options = new HtmlToImageOptions
		{
			ExcludeCssClasses = ["screenshot-ignore"],
			ExcludeSelector = "[data-private]"
		};

		// Act
		var json = JsonSerializer.Serialize(options);

		// Assert
		json.ShouldContain("\"excludeCssClasses\"");
		json.ShouldContain("\"excludeSelector\"");
	}
}
