using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Overlay;

/// <summary>An overlay fading as the pointer comes near, Core's <see cref="PointerFade"/> read 10 times a second while it is on.</summary>
internal sealed class OverlayFade(HWND hwnd, Func<Position> cursor) : IDisposable
{
    /// <summary>The id of the overlay window's timer that reads the pointer.</summary>
    internal const nuint TimerId = 2;

    private const uint Every = 100;

    /// <summary>The opacity the overlay shows at, 255 for whole, which every paint keeps.</summary>
    public byte Alpha { get; private set; } = 255;

    public void Dispose() => PInvoke.KillTimer(hwnd, TimerId);

    /// <summary>
    /// After a paint at <paramref name="scale"/>: the pointer is read only while the overlay may fade, so one without it
    /// costs nothing.
    /// </summary>
    public void Watch(OverlayConfig overlay, float? scale, bool windowOpen, bool held, bool shown)
    {
        if (overlay.Fade && !overlay.ClickThrough)
        {
            PInvoke.SetTimer(hwnd, TimerId, Every, null);
        }
        else
        {
            PInvoke.KillTimer(hwnd, TimerId);
        }

        Now(overlay, scale, windowOpen, held, shown);
    }

    /// <summary>The opacity for where the pointer is now, given to the window at once when it shows; none before a paint, without a scale.</summary>
    public void Now(OverlayConfig overlay, float? scale, bool windowOpen, bool held, bool shown)
    {
        if (scale is not { } painted || !PInvoke.GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var at = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
        var opacity = PointerFade.Opacity(overlay, at, cursor(), painted, windowOpen, held);
        var alpha = (byte)Math.Round(opacity * 255);
        if (alpha == Alpha)
        {
            return;
        }

        Alpha = alpha;
        if (shown)
        {
            // Without a source the layered window keeps its picture and takes only the new opacity.
            var blend = new BLENDFUNCTION { SourceConstantAlpha = alpha, AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA };
            PInvoke.UpdateLayeredWindow(hwnd, default, null, null, default, null, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        }
    }
}
