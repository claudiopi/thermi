using System.Buffers.Binary;

namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// A picture's pixels, from what <c>thermi preview</c> or the screen's capture wrote: macOS's own <c>sips</c> turns it into
/// an uncompressed BMP, which needs no library to read.
/// </summary>
internal sealed class Picture
{
    private readonly uint[] _pixels;

    private Picture(int width, int height, uint[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The PNG at <paramref name="path"/>, as <c>sips</c> turned it into a BMP beside it.</summary>
    public static async Task<Picture> ReadAsync(OwnSession session, string path, CancellationToken cancel)
    {
        var bitmap = Path.ChangeExtension(path, ".bmp");
        var turned = await session.RunAsync("sips", ["-s", "format", "bmp", path, "--out", bitmap], cancel: cancel).ConfigureAwait(false);
        return turned.Exit == 0
            ? FromBitmap(await File.ReadAllBytesAsync(bitmap, cancel).ConfigureAwait(false))
            : throw new InvalidOperationException($"sips did not read {path}: {turned.Error}");
    }

    /// <summary>How many pixels differ from <paramref name="other"/>'s, over the whole of a picture of the same size.</summary>
    public int Differing(Picture other) => Width == other.Width && Height == other.Height
        ? _pixels.Where((pixel, at) => pixel != other._pixels[at]).Count()
        : throw new InvalidOperationException($"A picture of {Width}x{Height} compared with one of {other.Width}x{other.Height}.");

    /// <summary>How bright the picture is at its middle, from 0 for black to 255 for white, as the eye weighs its colors.</summary>
    public int Brightness()
    {
        var pixel = _pixels[(Height / 2 * Width) + (Width / 2)];
        return (int)((0.299 * ((pixel >> 16) & 0xFF)) + (0.587 * ((pixel >> 8) & 0xFF)) + (0.114 * (pixel & 0xFF)));
    }

    /// <summary>A pixel where the picture draws nothing, wholly transparent, nearest its middle: where a click passes through.</summary>
    public (int X, int Y) Nothing()
    {
        var clear = Enumerable.Range(0, _pixels.Length).Where(at => _pixels[at] >> 24 == 0).Select(at => (X: at % Width, Y: at / Width)).ToList();
        return clear.Count > 0
            ? clear.MinBy(pixel => Math.Abs(pixel.X - (Width / 2)) + Math.Abs(pixel.Y - (Height / 2)))
            : throw new InvalidOperationException("The picture draws everywhere.");
    }

    // A BMP's pixels start where its header says, in rows from the bottom up unless its height is negative, each of 3 or 4
    // bytes a pixel, blue first, and padded to 4 bytes.
    private static Picture FromBitmap(byte[] bmp)
    {
        var start = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18));
        var height = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22));
        var bytes = BinaryPrimitives.ReadUInt16LittleEndian(bmp.AsSpan(28)) / 8;
        var stride = ((width * bytes) + 3) & ~3;
        var rows = Math.Abs(height);
        var pixels = new uint[width * rows];
        for (var row = 0; row < rows; row++)
        {
            var from = start + ((height > 0 ? rows - 1 - row : row) * stride);
            for (var column = 0; column < width; column++)
            {
                var at = from + (column * bytes);
                var alpha = bytes == 4 ? bmp[at + 3] : (byte)255;
                pixels[(row * width) + column] = (uint)((alpha << 24) | (bmp[at + 2] << 16) | (bmp[at + 1] << 8) | bmp[at]);
            }
        }

        return new Picture(width, rows, pixels);
    }
}
