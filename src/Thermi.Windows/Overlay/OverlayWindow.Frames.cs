using System.Drawing;
using Thermi.App.Painting;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Windows.Gestures;
using Thermi.Windows.Painting;
using Windows.Win32;

namespace Thermi.Windows.Overlay;

// What the overlay's window shows, painted only when it would show something else, and its own frames resting while
// nobody can see it.
internal sealed partial class OverlayWindow
{
    // What the window shows now, which a paint that would show the same leaves there.
    private OverlayFrame? _frame;

    /// <summary>Stops the glow's frames and the fade's reads while nobody can see the overlay; its next paint starts them again.</summary>
    public void Rest()
    {
        PInvoke.KillTimer(Hwnd, GlowTimer);
        PInvoke.KillTimer(Hwnd, OverlayFade.TimerId);
    }

    private OverlayFrame FrameOf(OverlayConfig overlay, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, string? stopped) =>
        new(items, look, scale, WindowPosition())
        {
            Overlay = overlay,
            Stopped = stopped,
            Marks = (Marks.Selected, Marks.Pointed, Marks.Accent),
            Alpha = _fade.Alpha,
            Moving = _glow.Glowing || Dragging || _gestures.Holds,
        };

    // Placed first as the last paint's size, which a frame of the same items, look and scale keeps, so a frame that only
    // moves still paints. The last one is where the window is now, since something else may have moved it, as Windows does
    // with a monitor that goes, and only a paint puts it back.
    private void Show(OverlayFrame frame, OverlayConfig overlay, Rgb accent, Func<Size, Position> place)
    {
        frame = _painted is { } last ? frame with { At = place(last.Size) } : frame;
        if (OnScreen && _frame is { } shown && !frame.Differs(shown with { At = WindowPosition() }))
        {
            return;
        }

        var bitmap = Painted(frame, overlay, accent);
        _frame = frame;
        _picture.Show(Hwnd, bitmap, place(bitmap.Size), _fade.Alpha);
    }

    private Bitmap Painted(OverlayFrame frame, OverlayConfig overlay, Rgb accent)
    {
#pragma warning disable CA2000 // The painter reuses the bitmap while its size holds, and disposes it itself.
        var bitmap = frame.Stopped is { } stopped
            ? _painter.PaintStopped(stopped, frame.Look, frame.Scale, frame.Items)
            : _painter.Paint(frame.Items, frame.Look, frame.Scale, _glow.Glowing ? _glow.Of : null);
#pragma warning restore CA2000
        IReadOnlyList<PaintedItem> places = frame.Stopped is null ? _painter.Places : [];
        Marks.Draw(bitmap, places, frame.Items, frame.Scale);
        _gestures.Mark(bitmap, places, frame.Scale, frame.Look.Orientation == Orientation.Vertical, accent);
        OverlayGestures.HoldCorner(bitmap, overlay, frame.Scale);
        _painted = new PaintedOverlay(frame.Items, frame.Look, frame.Scale, bitmap.Size) { Stopped = frame.Stopped is not null };
        return bitmap;
    }
}
