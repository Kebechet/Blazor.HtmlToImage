using Shouldly;
using Xunit;

namespace Kebechet.Blazor.HtmlToImage.Tests;

/// <summary>
/// The interop sends pixel data as an 8-byte header - width and height, little-endian uint32 - then
/// the RGBA bytes. Both sides of that wire contract have to agree byte for byte.
/// </summary>
public class PixelDataTests
{
    [Fact]
    public void FromInteropBytes_HeaderThenPixels_ReadsTheDimensionsAndKeepsOnlyThePixels()
    {
        // Arrange
        // 3x2 canvas: width 3 and height 2 as little-endian uint32, then 24 RGBA bytes.
        byte[] pixels = [.. Enumerable.Range(1, 24).Select(x => (byte)x)];
        byte[] interopBytes = [3, 0, 0, 0, 2, 0, 0, 0, .. pixels];

        // Act
        var pixelData = PixelData.FromInteropBytes(interopBytes);

        // Assert
        pixelData.Width.ShouldBe(3);
        pixelData.Height.ShouldBe(2);
        pixelData.Bytes.ShouldBe(pixels);
    }

    [Fact]
    public void FromInteropBytes_DimensionsAboveOneByte_ReadsThemLittleEndian()
    {
        // Arrange
        // 300x1 canvas: 300 is 0x012C, so a big-endian reader would see a different width.
        byte[] interopBytes = [0x2C, 0x01, 0, 0, 1, 0, 0, 0, .. new byte[300 * 4]];

        // Act
        var pixelData = PixelData.FromInteropBytes(interopBytes);

        // Assert
        pixelData.Width.ShouldBe(300);
        pixelData.Height.ShouldBe(1);
    }

    [Fact]
    public void FromInteropBytes_PixelsShorterThanTheHeaderClaims_Throws()
    {
        // Arrange
        byte[] interopBytes = [2, 0, 0, 0, 2, 0, 0, 0, .. new byte[12]];

        // Act & Assert
        // A truncated transfer must fail loudly, not hand back an image with missing rows.
        Should.Throw<InvalidOperationException>(() => PixelData.FromInteropBytes(interopBytes));
    }
}
