using System.Drawing;
using Thermi.App.Overlay;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Overlay.Tips;

/// <summary>
/// An overlay's value tooltip as a picture of its own on the desktop's surfaces, which offer none an overlay can use:
/// painted for the overlay's monitor and placed where App's <see cref="OverlayTips.Place"/> says, from the pointer's x,
/// above the overlay or below it. It takes no click, so the pointer stays on the overlay under it.
/// </summary>
internal sealed class TipShow(int index, OverlayParts parts, DesktopOverlay overlay) : ITipsHost
{
    // Past every overlay's, every mark's and the lifted pill's.
    private uint Id => uint.MaxValue - 2 - (uint)index;

    private bool _shown;

    public TimeProvider Time => parts.Time;

    public void Post(Action action) => parts.Post(action);

    public void Show(ShownTip? tip)
    {
        if (tip is null || overlay.Box is not { } box || parts.Screens() is not { Monitors.Count: > 0 } screens)
        {
            Hide();
            return;
        }

        var monitor = overlay.Monitor;
        using var picture = TipPainter.Paint(tip.Text, monitor, parts.Look.Dark, parts.Look.HighContrast);
        using GLib.Bytes pixels = picture.Bytes();
        var toThermi = screens.Scale / monitor;
        var size = new Size((int)Math.Round(picture.Width * toThermi), (int)Math.Round(picture.Height * toThermi));
        var screen = screens.Screens.FirstOrDefault(each => each.Bounds.Contains(new Position(box.Left, box.Top)))?.Bounds ?? box;
        var at = OverlayTips.Place(box, size, parts.Cursor?.At.X ?? box.Left, screen);
        var (x, y) = screens.Stage(at);
        _shown = true;
        _ = parts.Call(parts.Surfaces.Show(Id, new ShellPicture(pixels, picture.Width, picture.Height, picture.Stride, monitor, []), x, y));
    }

    public void Hide()
    {
        if (_shown)
        {
            _shown = false;
            _ = parts.Call(parts.Surfaces.Hide(Id));
        }
    }
}
