using System.Drawing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Windows.Gestures;
using Thermi.Windows.Logging;
using Thermi.Windows.Painting;
using Windows.Win32;

namespace Thermi.Windows.Overlay;

// What the overlay's window shows, drawn only when it would show something else, and its own frames resting while
// nobody can see it.
internal sealed partial class OverlayWindow
{
    // The one record of the last draw, and the size of its picture, which a hidden overlay keeps for the tray to copy.
    private OverlayFrame? _frame;
    private Size _drawn;
    private bool _drawAgain;

    // What the last draw drew, as the gestures and the overlay's picture read it.
    private PaintedOverlay? Painted =>
        _frame is { } frame ? new PaintedOverlay(frame.Items, frame.Look, frame.Scale, _drawn) { Stopped = frame.Stopped is not null } : null;

    /// <summary>Stops the glow's frames and the fade's reads while nobody can see the overlay; its next paint starts them again.</summary>
    public void Rest()
    {
        PInvoke.KillTimer(Hwnd, GlowTimer);
        PInvoke.KillTimer(Hwnd, OverlayFade.TimerId);
    }

    /// <summary>The next paint draws whatever it shows, though the last draw is kept till then.</summary>
    public void DrawAgain() => _drawAgain = true;

    private OverlayFrame FrameOf(OverlayConfig overlay, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, string? stopped) =>
        new(items, look, scale, WindowPosition())
        {
            Overlay = overlay,
            Stopped = stopped,
            Marks = (Marks.Selected, Marks.Pointed, Marks.Accent),
            Alpha = _fade.Alpha,
            Moving = _glow.Glowing || Dragging || _gestures.Holds,
        };

    // Placed first as the last draw's size, which a frame of the same items, look and scale keeps, so a frame that only
    // moves still draws. The last one is where the window is now, since something else may have moved it, as Windows does
    // with a monitor that goes, and only a draw puts it back.
    private void Show(OverlayFrame frame, OverlayConfig overlay, Rgb accent, Func<Size, Position> place)
    {
        frame = _frame is not null ? frame with { At = place(_drawn) } : frame;
        if (!_drawAgain && OnScreen && _frame is { } shown && !frame.Differs(shown with { At = WindowPosition() }))
        {
            return;
        }

        var bitmap = Draw(frame, overlay, accent);
        (_frame, _drawn, _drawAgain) = (frame, bitmap.Size, false);
        _picture.Show(Hwnd, bitmap, place(bitmap.Size), _fade.Alpha);
        _log.Drew();
    }

    private Bitmap Draw(OverlayFrame frame, OverlayConfig overlay, Rgb accent)
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
        return bitmap;
    }
}
