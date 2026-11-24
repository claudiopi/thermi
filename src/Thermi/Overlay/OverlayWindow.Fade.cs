using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Overlay;

/// <summary>The overlay fading as the pointer comes near, Core's <see cref="PointerFade"/> read 10 times a second while it is on.</summary>
internal sealed partial class OverlayWindow
{
    private const nuint FadeTimer = 2;
    private const uint FadeEvery = 100;

    // The opacity the overlay shows at, 255 for whole, which every paint keeps.
    private byte _alpha = 255;

    // The pointer is only read while the overlay may fade, so an overlay without it costs nothing.
    private void WatchPointer(OverlayConfig overlay)
    {
        if (overlay.Fade && !overlay.ClickThrough)
        {
            PInvoke.SetTimer(Hwnd, FadeTimer, FadeEvery, null);
        }
        else
        {
            PInvoke.KillTimer(Hwnd, FadeTimer);
        }

        FadeNow(overlay);
    }

    private void FadeNow(OverlayConfig overlay)
    {
        if (_painted is not { } painted || !PInvoke.GetWindowRect(Hwnd, out var rect))
        {
            return;
        }

        var at = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
        var opacity = PointerFade.Opacity(overlay, at, _actions.Cursor(), painted.Scale, _scene?.WindowOpen == true, _drag is not null);
        var alpha = (byte)Math.Round(opacity * 255);
        if (alpha == _alpha)
        {
            return;
        }

        _alpha = alpha;
        if (_shown)
        {
            // Without a source the layered window keeps its picture and takes only the new opacity.
            var blend = new BLENDFUNCTION { SourceConstantAlpha = alpha, AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA };
            PInvoke.UpdateLayeredWindow(Hwnd, default, null, null, default, null, default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        }
    }
}
