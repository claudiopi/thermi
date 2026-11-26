using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

namespace Thermi.Windows.Win32.Clipboard;

/// <summary>A picture put on Windows' clipboard, as a PNG for the apps that keep its transparency and as a bitmap for the rest.</summary>
internal static class ClipboardImage
{
    private const int HeaderSize = 40;

    private static readonly uint Png = PInvoke.RegisterClipboardFormat("PNG");

    /// <summary>Takes the clipboard for <paramref name="owner"/> and leaves the picture on it; nothing when another app keeps it open.</summary>
    public static void Write(HWND owner, Bitmap picture)
    {
        using var png = new MemoryStream();
        picture.Save(png, ImageFormat.Png);
        var dib = Dib(picture);
        _ = ClipboardData.Write(owner, () =>
        {
            var both = ClipboardData.Put(Png, png.ToArray());
            return ClipboardData.Put((uint)CLIPBOARD_FORMAT.CF_DIB, dib) && both;
        });
    }

    // A 32-bit bitmap's header and its rows, bottom row first, as a DIB keeps them.
    private static byte[] Dib(Bitmap picture)
    {
        var (width, height) = (picture.Width, picture.Height);
        var bytes = new byte[HeaderSize + (width * height * 4)];
        using (var header = new BinaryWriter(new MemoryStream(bytes)))
        {
            header.Write(HeaderSize);
            header.Write(width);
            header.Write(height);
            header.Write((short)1);
            header.Write((short)32);
        }

        var rows = picture.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(rows.Scan0 + (row * rows.Stride), bytes, HeaderSize + ((height - 1 - row) * width * 4), width * 4);
            }
        }
        finally
        {
            picture.UnlockBits(rows);
        }

        return bytes;
    }
}
