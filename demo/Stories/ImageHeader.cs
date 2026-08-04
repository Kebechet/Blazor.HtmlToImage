namespace Blazor.HtmlToImage.Demo.Stories;

/// <summary>
/// Minimal image-header reading, so a story can report the real pixel dimensions of what was
/// captured rather than what the DOM measured. That difference is the whole point of
/// <c>PixelRatio</c>, and the browser suite asserts against it.
/// </summary>
internal static class ImageHeader
{
    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Byte offset of the IHDR chunk's width field: 8-byte signature + 4-byte length + 4-byte type.</summary>
    private const int _ihdrWidthOffset = 16;

    internal static bool IsPng(byte[] bytes)
    {
        if (bytes.Length < _pngSignature.Length)
        {
            return false;
        }

        for (var index = 0; index < _pngSignature.Length; index++)
        {
            if (bytes[index] != _pngSignature[index])
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsJpeg(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
    }

    /// <summary>
    /// Reads the pixel dimensions from a PNG's IHDR chunk, or null when the bytes are not a PNG.
    /// The fields are big-endian, which is PNG's own byte order and not the host's.
    /// </summary>
    internal static ImageSize? ReadPngSize(byte[] bytes)
    {
        if (!IsPng(bytes) || bytes.Length < _ihdrWidthOffset + 8)
        {
            return null;
        }

        return new ImageSize(
            ReadBigEndianInt32(bytes, _ihdrWidthOffset),
            ReadBigEndianInt32(bytes, _ihdrWidthOffset + 4));
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }
}

internal readonly record struct ImageSize(int Width, int Height);
