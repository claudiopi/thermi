using System.Drawing;
using System.Drawing.Imaging;
using Thermi.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Painting;

/// <summary>
/// What a layered window shows, in a bitmap of Windows' own kept while its size holds: each paint copies the painter's
/// picture into it, premultiplied as Windows wants it, instead of making a new one, as a glow's 30 frames a second would.
/// Disposed on the thread that shows it.
/// </summary>
internal sealed unsafe class LayeredPicture : IDisposable
{
    private HDC _memory;
    private HBITMAP _bitmap;
    private HGDIOBJ _previous;
    private void* _bits;
    private Size _size;

    /// <summary>
    /// Puts <paramref name="picture"/> on screen at <paramref name="position"/>: its alpha becomes the window's shape,
    /// rounded corners and all, and where clicks pass. Moving and resizing in the same call keeps a snapped edge still as
    /// the overlay grows.
    /// </summary>
    public void Show(HWND window, Bitmap picture, Position position, byte alpha = 255)
    {
        Fit(picture.Size);
        var copy = new BitmapData
        {
            Width = _size.Width,
            Height = _size.Height,
            Stride = _size.Width * 4,
            PixelFormat = PixelFormat.Format32bppPArgb,
            Scan0 = (nint)_bits,
        };
        // Into the buffer given, Windows' bitmap, converted on the way: ImageLockMode's values are flags, though not marked so.
        var into = (ImageLockMode)((int)ImageLockMode.ReadOnly | (int)ImageLockMode.UserInputBuffer);
        picture.LockBits(new Rectangle(Point.Empty, _size), into, PixelFormat.Format32bppPArgb, copy);
        picture.UnlockBits(copy);
        var blend = new BLENDFUNCTION { SourceConstantAlpha = alpha, AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA };
        PInvoke.UpdateLayeredWindow(
            window,
            default,
            new Point(position.X, position.Y),
            new SIZE(_size.Width, _size.Height),
            _memory,
            new Point(0, 0),
            default,
            blend,
            UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
    }

    public void Dispose() => Release();

    // A top-down 32-bit bitmap, so its rows lie in the order the picture's do.
    private void Fit(Size size)
    {
        if (size == _size && !_memory.IsNull)
        {
            return;
        }

        Release();
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = size.Width,
                biHeight = -size.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = (uint)BI_COMPRESSION.BI_RGB,
            },
        };
        void* bits;
        _bitmap = PInvoke.CreateDIBSection(HDC.Null, &info, DIB_USAGE.DIB_RGB_COLORS, &bits, HANDLE.Null, 0);
        _memory = PInvoke.CreateCompatibleDC(HDC.Null);
        _previous = PInvoke.SelectObject(_memory, (HGDIOBJ)_bitmap.Value);
        _bits = bits;
        _size = size;
    }

    private void Release()
    {
        if (!_memory.IsNull)
        {
            PInvoke.SelectObject(_memory, _previous);
            PInvoke.DeleteDC(_memory);
            _memory = HDC.Null;
        }

        if (!_bitmap.IsNull)
        {
            PInvoke.DeleteObject((HGDIOBJ)_bitmap.Value);
            _bitmap = HBITMAP.Null;
        }

        _bits = null;
        _size = Size.Empty;
    }
}
