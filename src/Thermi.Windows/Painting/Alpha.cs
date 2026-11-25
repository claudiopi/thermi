using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Thermi.Windows.Painting;

/// <summary>
/// Fades what is painted. A layered window lets clicks through its fully transparent pixels only, so every pixel that
/// was painted keeps an alpha of at least 1/255: a pill catches clicks at any opacity, and only click-through lets them pass.
/// </summary>
internal static class Alpha
{
    /// <summary>Multiplies the alpha of every painted pixel by <paramref name="factor"/>, 0 to 1, never below 1/255.</summary>
    public static void Scale(Bitmap bitmap, double factor)
    {
        if (factor >= 1)
        {
            return;
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var length = data.Width * 4;
        var row = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            for (var y = 0; y < data.Height; y++)
            {
                var at = data.Scan0 + (y * data.Stride);
                Marshal.Copy(at, row, 0, length);
                Fade(row.AsSpan(0, length), factor);
                Marshal.Copy(row, 0, at, length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(row);
            bitmap.UnlockBits(data);
        }
    }

    private static void Fade(Span<byte> pixels, double factor)
    {
        for (var alpha = 3; alpha < pixels.Length; alpha += 4)
        {
            pixels[alpha] = pixels[alpha] == 0 ? (byte)0 : (byte)Math.Max(1, Math.Round(pixels[alpha] * factor));
        }
    }
}
