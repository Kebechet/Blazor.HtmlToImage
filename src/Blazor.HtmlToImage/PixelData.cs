using System.Buffers.Binary;

namespace Kebechet.Blazor.HtmlToImage;

/// <summary>
/// The raw RGBA pixels of a capture, with the dimensions needed to interpret them.
/// </summary>
public sealed class PixelData
{
    /// <summary>
    /// Size of the header html-to-image-interop.js writes in front of the pixels: the canvas width
    /// and height as little-endian <c>uint32</c>. A wire contract with that file's <c>toStream</c>.
    /// </summary>
    private const int _headerBytes = 8;

    /// <summary>Creates pixel data from its dimensions and RGBA bytes.</summary>
    public PixelData(int width, int height, byte[] bytes)
    {
        Width = width;
        Height = height;
        Bytes = bytes;
    }

    /// <summary>Width of the captured canvas in pixels - the element's width times the pixel ratio.</summary>
    public int Width { get; }

    /// <summary>Height of the captured canvas in pixels - the element's height times the pixel ratio.</summary>
    public int Height { get; }

    /// <summary>
    /// Four bytes per pixel - red, green, blue, alpha - row by row from the top-left corner, so
    /// <c>Width * Height * 4</c> long.
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>Splits the interop's header from the pixels it precedes.</summary>
    internal static PixelData FromInteropBytes(byte[] interopBytes)
    {
        if (interopBytes.Length < _headerBytes)
        {
            throw new InvalidOperationException($"Pixel data is {interopBytes.Length} bytes, shorter than its {_headerBytes}-byte header.");
        }

        var width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(interopBytes.AsSpan(0, 4)));
        var height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(interopBytes.AsSpan(4, 4)));
        var pixelBytes = interopBytes.AsSpan(_headerBytes).ToArray();
        if (pixelBytes.Length != (long)width * height * 4)
        {
            throw new InvalidOperationException($"Pixel data for {width}x{height} should be {(long)width * height * 4} bytes, got {pixelBytes.Length}.");
        }

        return new PixelData(width, height, pixelBytes);
    }
}
