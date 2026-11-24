using System.Drawing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Painting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Gestures;

/// <summary>What an overlay's last paint drew, which a pill carried off is painted from.</summary>
internal sealed record PaintedOverlay(IReadOnlyList<OverlayItem> Items, OverlayLook Look, float Scale, Size Size)
{
    /// <summary>Whether the "Sensors stopped" pill led the items.</summary>
    public bool Stopped { get; init; }
}

/// <summary>
/// An overlay's desktop gestures: the overlay moved onto a snap mark or into another overlay, a pill held still until it
/// lifts and then dragged off, and a floating overlay's corner. The window says where it is and what it drew; these decide
/// what a press does.
/// </summary>
internal sealed class OverlayGestures(int index, Settings settings, OverlayActions actions, HWND hwnd) : IDisposable
{
    /// <summary>The timer that wakes the overlay when a press on a pill may have held long enough to lift it.</summary>
    internal const nuint HoldTimer = 3;

    // The corner of a floating overlay that resizes it, in pixels at 100 %, square.
    private const float CornerSize = 14;

    // When the press began, on the injected clock, which says whether it held long enough.
    private long _pressed;

    /// <summary>The drag a press holds, which the overlay is painted as, or null.</summary>
    public DesktopDrag? Drag { get; private set; }

    public void Dispose() => Let();

    /// <summary>Where the overlay goes while a press holds it: where the drag has it, unless it carries a pill off or holds the corner.</summary>
    public Position? HeldAt(Position? dragged) => Drag?.Moves == false ? null : dragged;

    /// <summary>
    /// A press on <paramref name="overlay"/> at <paramref name="at"/>: on its corner, its edge, or the pill of the item it
    /// is on, in its box. On a pill it starts the wait for the hold that lifts it.
    /// </summary>
    public void Press(OverlayConfig overlay, PaintedOverlay painted, IReadOnlyList<Screen> screens, (string? Item, RectangleF Box) on, Position at)
    {
        Let();
        var item = on.Item;
        var held = InCorner(overlay, painted, at, actions.Cursor()) ? Held.Corner : item is null ? Held.Edge : Held.Pill;
        var start = new DragStart(held, item, on.Box, at, painted.Size, painted.Scale, item is null ? null : () => PillPicture(item, painted));
        Drag = new DesktopDrag(index, settings, start, screens, hwnd);
        _pressed = actions.Time.GetTimestamp();
        if (held == Held.Pill)
        {
            Wake(DesktopGestures.Hold);
        }
    }

    /// <summary>The hold's timer woke the overlay: the pill lifts once the clock says the press held long enough, or it waits on.</summary>
    public void Hold()
    {
        PInvoke.KillTimer(hwnd, HoldTimer);
        var held = actions.Time.GetElapsedTime(_pressed);
        if (Drag is null)
        {
            return;
        }

        if (held < DesktopGestures.Hold)
        {
            Wake(DesktopGestures.Hold - held);
            return;
        }

        Drag.Lift(held);
    }

    private void Wake(TimeSpan after) => PInvoke.SetTimer(hwnd, HoldTimer, (uint)Math.Ceiling(after.TotalMilliseconds), null);

    /// <summary>The pointer dragged to <paramref name="to"/>: the overlay follows unless the press carries a pill or holds the corner.</summary>
    public void Move(Position to, Position? dragged)
    {
        if (Drag?.Moves != false)
        {
            PInvoke.SetWindowPos(
                hwnd,
                HWND.Null,
                to.X,
                to.Y,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }

        var over = dragged is not null && actions.OverlayAt(index, actions.Cursor()) is not null;
        if (Drag?.Moved(dragged, over) == true)
        {
            actions.RequestPaint();
        }
    }

    /// <summary>A drag dropped onto another overlay merges into it; the corner's preview ends with the press either way.</summary>
    public void Release(Press press, DragDistance distance)
    {
        var cursor = actions.Cursor();
        var merging = Drag?.Merging == true;
        var onto = actions.OverlayAt(index, cursor);
        Drag?.Release(press, cursor, distance, new DropTarget(onto, onto is { } target ? actions.ItemAfter(target, cursor) : null), actions.Gestures);
        Let();
        // A merge refused writes nothing that would paint it whole again.
        if (merging)
        {
            actions.RequestPaint();
        }
    }

    /// <summary>Whether Esc was down as the message being handled was sent, which cancels a drag.</summary>
    public static bool Escape() => PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_ESCAPE) < 0;

    /// <summary>Lets the mouse go after Esc cancelled the press; the change of capture that follows finds no press.</summary>
    public void LetMouseGo()
    {
        if (PInvoke.GetCapture() == hwnd)
        {
            PInvoke.ReleaseCapture();
        }
    }

    /// <summary>Ends the press's drag, whatever it did.</summary>
    public void Let()
    {
        PInvoke.KillTimer(hwnd, HoldTimer);
        Drag?.Dispose();
        Drag = null;
    }

    /// <summary>A double-click on the corner, which sets the overlay back to its own size; false anywhere else.</summary>
    public bool DoubleClickedCorner(OverlayConfig overlay, PaintedOverlay? painted, Position at)
    {
        if (!InCorner(overlay, painted, at, actions.Cursor()))
        {
            return false;
        }

        var before = settings.Config;
        var after = OverlayEdits.Resize(before, index, 1, overlay.Style.Orientation);
        if (after.Refused is null && ConfigEdits.Replay(before, after.Config) is { } change)
        {
            settings.Edit(change);
            actions.Gestures?.Add(GestureKind.Resize, before, after.Config);
        }

        return true;
    }

    /// <summary>Whether <paramref name="cursor"/> is on the resizing corner of a floating overlay at <paramref name="at"/>.</summary>
    public static bool InCorner(OverlayConfig overlay, PaintedOverlay? painted, Position at, Position cursor)
    {
        if (overlay.Snapped is not null || overlay.Locked || painted is null)
        {
            return false;
        }

        var side = CornerSize * painted.Scale;
        var (x, y) = (cursor.X - at.X, cursor.Y - at.Y);
        return x >= painted.Size.Width - side && y >= painted.Size.Height - side && x < painted.Size.Width && y < painted.Size.Height;
    }

    /// <summary>Takes back the last desktop gesture, as the overlay's menu offers.</summary>
    public void Undo()
    {
        if (actions.Gestures?.Take() is { } undo)
        {
            settings.Edit(undo);
            actions.RequestPaint();
        }
    }

    private Bitmap PillPicture(string item, PaintedOverlay painted)
    {
        var ids = DesktopGestures.PillItems(settings.Config, item);
        // Carried over other pills, it keeps a background of its own whatever the look's opacity or the window's fade.
        using var painter = new Painter();
        return painter.Paint(
            [.. painted.Items.Where(each => ids.Contains(each.Id))],
            painted.Look with { Orientation = Orientation.Horizontal, Background = 1, Opacity = 1 },
            painted.Scale);
    }
}
