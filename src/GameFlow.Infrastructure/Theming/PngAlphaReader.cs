using System.Buffers.Binary;
using System.IO.Compression;

namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// Reads the alpha channel of a PNG file, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The touchpad silhouette (<see cref="AlphaSilhouette"/>) has to be known
/// in two places: the dashboard's <c>ThemeSurface</c>, which has Avalonia
/// to decode with, and the OBS overlay's <c>OverlayProgramBuilder</c>,
/// which lives here in Infrastructure and has no image library at all.
/// Decoding in Avalonia for one and in the browser for the other would be
/// two implementations of the same measurement that are free to disagree,
/// and the overlay must never draw a finger somewhere the app does not.
/// So the one measurement lives here, and both read it.
/// </para>
/// <para>
/// Only what theme packs actually ship is supported: non-interlaced PNG in
/// any colour type and bit depth. An interlaced or malformed file returns
/// false, and the caller falls back to the image rectangle — the
/// behaviour every theme had before this existed — rather than guessing.
/// CRCs are not verified: a corrupt chunk still fails in the inflater or
/// the filter step, and the result only ever narrows a rectangle.
/// </para>
/// </remarks>
public static class PngAlphaReader
{
    /// <summary>
    /// Largest image read, in pixels. A touchpad overlay is well under a
    /// megapixel; this only stops a hostile or broken pack from asking for
    /// gigabytes.
    /// </summary>
    private const long MaximumPixels = 64L * 1024 * 1024;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads the alpha channel of the PNG at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">PNG file to read.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="alpha">
    /// One byte per pixel, row-major, 0 transparent to 255 opaque. Null when
    /// the image has no transparency at all (an RGB or greyscale image with
    /// no <c>tRNS</c> chunk), which means every pixel is opaque.
    /// </param>
    /// <returns>False when the file is missing, not a PNG, interlaced or malformed.</returns>
    public static bool TryRead(string path, out int width, out int height, out byte[]? alpha)
    {
        width = 0;
        height = 0;
        alpha = null;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        return TryRead(bytes, out width, out height, out alpha);
    }

    /// <summary>Reads the alpha channel of an in-memory PNG. See <see cref="TryRead(string, out int, out int, out byte[])"/>.</summary>
    public static bool TryRead(ReadOnlySpan<byte> png, out int width, out int height, out byte[]? alpha)
    {
        width = 0;
        height = 0;
        alpha = null;

        try
        {
            return TryDecode(png, out width, out height, out alpha);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException or ArgumentException or IndexOutOfRangeException)
        {
            width = 0;
            height = 0;
            alpha = null;
            return false;
        }
    }

    private static bool TryDecode(ReadOnlySpan<byte> png, out int width, out int height, out byte[]? alpha)
    {
        width = 0;
        height = 0;
        alpha = null;

        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
        {
            return false;
        }

        int bitDepth = 0, colorType = -1, interlace = 0;
        byte[]? transparency = null;
        using var compressed = new MemoryStream();

        var offset = Signature.Length;
        var sawHeader = false;
        while (offset + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]);
            var type = png.Slice(offset + 4, 4);
            var dataStart = offset + 8;
            if (length < 0 || dataStart + (long)length + 4 > png.Length)
            {
                return false;
            }

            var data = png.Slice(dataStart, length);
            offset = dataStart + length + 4;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length < 13) { return false; }
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                bitDepth = data[8];
                colorType = data[9];
                interlace = data[12];
                sawHeader = true;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (!sawHeader || width <= 0 || height <= 0 || (long)width * height > MaximumPixels || interlace != 0)
        {
            return false;
        }

        var channels = colorType switch
        {
            0 => 1, // greyscale
            2 => 3, // RGB
            3 => 1, // palette index
            4 => 2, // greyscale + alpha
            6 => 4, // RGBA
            _ => 0,
        };
        if (channels == 0 || bitDepth is not (1 or 2 or 4 or 8 or 16))
        {
            return false;
        }

        // No alpha channel and no palette transparency: every pixel is
        // opaque, and there is nothing to inflate. A colour-key tRNS on an
        // RGB or greyscale image marks one exact colour as clear; theme
        // packs do not use it, and treating it as opaque only keeps the
        // whole rectangle — the behaviour before this reader existed.
        if (colorType is 0 or 2 || (colorType == 3 && transparency is null))
        {
            return true;
        }

        var bitsPerPixel = channels * bitDepth;
        var rowBytes = checked((int)(((long)width * bitsPerPixel + 7) / 8));
        var filterStride = Math.Max(1, bitsPerPixel / 8);
        var raw = Inflate(compressed, checked(height * (rowBytes + 1)));
        if (raw is null)
        {
            return false;
        }

        var result = new byte[width * height];
        var previous = new byte[rowBytes];
        var current = new byte[rowBytes];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (rowBytes + 1);
            raw.AsSpan(rowStart + 1, rowBytes).CopyTo(current);
            if (!Unfilter(raw[rowStart], current, previous, filterStride))
            {
                return false;
            }

            var target = result.AsSpan(y * width, width);
            switch (colorType)
            {
                case 6:
                    // RGBA: alpha is the last sample. At 16 bits the high
                    // byte is the first of the pair, which is all 8-bit
                    // precision needs.
                    for (var x = 0; x < width; x++)
                    {
                        target[x] = bitDepth == 16 ? current[(x * 8) + 6] : current[(x * 4) + 3];
                    }
                    break;

                case 4:
                    for (var x = 0; x < width; x++)
                    {
                        target[x] = bitDepth == 16 ? current[(x * 4) + 2] : current[(x * 2) + 1];
                    }
                    break;

                case 3:
                    for (var x = 0; x < width; x++)
                    {
                        var index = PaletteIndex(current, x, bitDepth);
                        target[x] = index < transparency!.Length ? transparency[index] : (byte)255;
                    }
                    break;
            }

            (previous, current) = (current, previous);
        }

        alpha = result;
        return true;
    }

    private static byte[]? Inflate(MemoryStream compressed, int expectedLength)
    {
        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        var buffer = new byte[expectedLength];
        var read = 0;
        while (read < expectedLength)
        {
            var count = inflater.Read(buffer, read, expectedLength - read);
            if (count == 0)
            {
                return null;
            }

            read += count;
        }

        return buffer;
    }

    /// <summary>Reverses one scanline's PNG filter in place.</summary>
    private static bool Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int stride)
    {
        switch (filter)
        {
            case 0:
                return true;

            case 1: // Sub
                for (var i = stride; i < row.Length; i++)
                {
                    row[i] = (byte)(row[i] + row[i - stride]);
                }
                return true;

            case 2: // Up
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = (byte)(row[i] + previous[i]);
                }
                return true;

            case 3: // Average
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= stride ? row[i - stride] : 0;
                    row[i] = (byte)(row[i] + ((left + previous[i]) >> 1));
                }
                return true;

            case 4: // Paeth
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= stride ? row[i - stride] : 0;
                    var upLeft = i >= stride ? previous[i - stride] : 0;
                    row[i] = (byte)(row[i] + Paeth(left, previous[i], upLeft));
                }
                return true;

            default:
                return false;
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpLeft = Math.Abs(estimate - upLeft);
        if (toLeft <= toUp && toLeft <= toUpLeft) { return left; }
        return toUp <= toUpLeft ? up : upLeft;
    }

    private static int PaletteIndex(ReadOnlySpan<byte> row, int x, int bitDepth)
    {
        if (bitDepth == 8)
        {
            return row[x];
        }

        // Sub-byte indices are packed from the most significant bit.
        var perByte = 8 / bitDepth;
        var packed = row[x / perByte];
        var shift = 8 - (bitDepth * ((x % perByte) + 1));
        return (packed >> shift) & ((1 << bitDepth) - 1);
    }
}
