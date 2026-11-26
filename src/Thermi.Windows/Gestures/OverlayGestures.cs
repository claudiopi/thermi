using System.Drawing;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Overlay;
using Thermi.Windows.Painting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Gestures;

/// <summary>What an overlay's last paint drew, which a pill carried off is painted from.</summary>
internal sealed record PaintedOverlay(IReadOnlyList<OverlayItem> Items, OverlayLook Look, float Scale, Size Size)
{
    /// <summary>Whether the "Sensors stopped" pill led the items.</summary>
    public bool Stopped { get; init; }

    /// <summary>
    /// The overlay painted afresh without what only the open window or a broken file adds; the "Sensors stopped" pill,
    /// with <paramref name="stopped"/> in it, while they are.
    /// </summary>
    public Bitmap Picture(string stopped)
    {
        using var painter = new Painter();
        IReadOnlyList<OverlayItem> items = [.. Items.Where(item => item.Id.Length > 0)];
        return Stopped || items.Count == 0 ? painter.PaintStopped(stopped, Look, Scale, items) : painter.Paint(items, Look, Scale);
    }
}

/// <summary>
/// An overlay's desktop gestures: the overlay moved onto a snap mark or into another overlay, a pill or an item held still
/// until it lifts and then dragged off, and a floating overlay's corner. The window says where it is and what it drew; these decide
/// what a press does.
/// </summary>
internal sealed class OverlayGestures(int index, Settings settings, OverlayActions actions, HWND hwnd) : IDisposable
{
    /// <summary>The timer that wakes the overlay when a press on a pill may have held long enough to lift it or its item.</summary>
    internal const nuint HoldTimer = 3;

    /// <summary>The id of Esc, which a press holds as a hotkey of the overlay's, so it cancels the drag and reaches no other app.</summary>
    internal const nuint EscapeKey = 1;

    // The corner of a floating overlay that resizes it, in pixels at 100 %, square.
    private const float CornerSize = 14;

    // When the press began, on the injected clock, which says whether it held long enough.
    private long _pressed;

    // Whether Esc is the overlay's hotkey now, which it lets go with the press.
    private bool _escape;

    // What the wheel's turns left short of a notch, which the next turn adds to.
    private int _turned;

    /// <summary>The drag a press holds, which the overlay is painted as, or null.</summary>
    public DesktopDrag? Drag { get; private set; }

    public void Dispose() => Let();

    /// <summary>Where the overlay goes while a press holds it: where the drag has it, unless it carries a pill off or holds the corner.</summary>
    public Position? HeldAt(Position? dragged) => Drag?.Moves == false ? null : dragged;

    /// <summary>Whether a drag holds the overlay or is over it, which keeps it whole, without its fade.</summary>
    public bool Holds => Drag is not null || actions.Preview.Onto == index;

    // A lifted pill leaves its overlay in place, which a drop back on it joins as any other.
    private int Self => Drag?.Lifted == true ? -1 : index;

    /// <summary>
    /// A press on <paramref name="overlay"/> at <paramref name="at"/>: on its corner, its edge, or the item it is on, in
    /// its pill and its own place, on its pill's label or not. On a pill it starts the wait for the hold that lifts the
    /// pill, or the item alone as Core's <see cref="DesktopGestures.Grip"/> says.
    /// </summary>
    public void Press(OverlayConfig overlay, PaintedOverlay painted, IReadOnlyList<Screen> screens, PressedOn on, Position at)
    {
        Let();
        var carried = DesktopGestures.Grip(settings.Config, on.Item, on.OnLabel);
        var held = InCorner(overlay, painted, at, actions.Cursor()) ? Held.Corner : carried is null ? Held.Edge : Held.Pill;
        var (box, gap, picture) = carried is { Alone: true } alone ? (on.Place, on.Alone, () => ItemPicture(alone.Item, painted))
            : carried is { } pill ? (on.Pill, on.Pill, () => PillPicture(pill.Item, painted))
            : (on.Pill, on.Pill, (Func<Bitmap>?)null);
        var start = new DragStart(held, carried, box, gap, at, painted.Size, painted.Scale, picture);
        Drag = new DesktopDrag(index, settings, start, screens, hwnd, actions.Settle);
        _pressed = actions.Time.GetTimestamp();
        _escape = PInvoke.RegisterHotKey(hwnd, (int)EscapeKey, HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)VIRTUAL_KEY.VK_ESCAPE);
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
        if (Drag.Lifted)
        {
            actions.RequestPaint();
        }
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

        var target = Drag is null or { Resizes: true } or { Refused: true } ? default : Target(actions.Cursor(), dragged);
        var moved = Drag?.Moved(dragged, target.Onto is not null) == true;
        // The overlay a drop would join marks where, which it paints.
        var previewed = actions.Preview.Over(target);
        if (previewed || moved)
        {
            actions.RequestPaint();
        }
    }

    /// <summary>A drag dropped onto an overlay joins it; the corner's preview ends with the press either way.</summary>
    public void Release(Press press, DragDistance distance)
    {
        var cursor = actions.Cursor();
        Drag?.Release(press, cursor, distance, Target(cursor, Drag.Dropped), actions.Gestures);
        Let();
    }

    // An item carried alone may land within its own pill, which only its own overlay can tell.
    private DropTarget Target(Position cursor, Position? dragged) =>
        dragged is { } at && Drag is { } drag
        && DesktopGestures.JoinTarget(actions.Shown(), Self, drag.Box(at), cursor, DesktopDrag.AltHeld()) is var (onto, point)
            ? actions.JoinAt(onto, point, drag is { Lifted: true, Carried: { Alone: true } alone } && onto == index ? alone.Item : null)
            : default;

    /// <summary>
    /// Whether Esc was down as the message being handled was sent, which cancels a drag: read as the mouse moves for when
    /// another app holds Esc as its hotkey.
    /// </summary>
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
        if (_escape)
        {
            PInvoke.UnregisterHotKey(hwnd, (int)EscapeKey);
            _escape = false;
        }

        Drag?.Dispose();
        Drag = null;
        if (actions.Preview.Over(default))
        {
            actions.RequestPaint();
        }
    }

    /// <summary>
    /// Marks what the drag shows on the overlay's last paint: the gap a lifted pill or item leaves, and where a drop joins
    /// it, between the items of its own pill for an item carried over it.
    /// </summary>
    public void Mark(Bitmap bitmap, IReadOnlyList<PaintedItem> places, float scale, bool vertical, Rgb accent)
    {
        if (Drag is { Lifted: true } drag)
        {
            DragMarks.Gap(bitmap, drag.Gap, scale);
        }

        if (actions.Preview.Onto == index)
        {
            var within = actions.Preview.InPill ? Drag?.Carried?.Item : null;
            DragMarks.Join(bitmap, places, (actions.Preview.Before, within), vertical, accent, scale);
        }
    }

    /// <summary>A double-click on the corner, which sets the overlay back to its own size; false anywhere else.</summary>
    public bool DoubleClickedCorner(OverlayConfig overlay, PaintedOverlay? painted, Position at)
    {
        if (!InCorner(overlay, painted, at, actions.Cursor()))
        {
            return false;
        }

        var before = settings.Config;
        var resized = OverlayEdits.Resize(before, index, 1, overlay.Style.Orientation);
        var after = resized.Refused is null ? actions.Settle(before, resized.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is { } change)
        {
            settings.Edit(change);
            actions.Gestures?.Add(GestureKind.Resize, before, after);
        }

        return true;
    }

    /// <summary>
    /// The wheel turned over the overlay, as <c>WM_MOUSEWHEEL</c>'s <paramref name="wParam"/> says: with Ctrl, the overlay
    /// takes the size Core's steps give, a notch within a second of the last joining its step, and true. A locked overlay
    /// keeps its size, as it keeps its corner, and so does one a press holds.
    /// </summary>
    public bool Wheel(nuint wParam, IReadOnlyList<Screen> screens)
    {
        var before = settings.Config;
        if ((wParam & (nuint)MODIFIERKEYS_FLAGS.MK_CONTROL) == 0)
        {
            return false;
        }

        if (Drag is null && index < before.Overlays.Count && !before.Overlays[index].Locked)
        {
            Resize(before, (short)(wParam >> 16), screens);
        }

        return true;
    }

    private void Resize(ThermiConfig before, int delta, IReadOnlyList<Screen> screens)
    {
        (var size, _turned) = OverlaySizes.Wheel(before.Overlays[index], screens, _turned, delta);
        var resized = size is { } to ? OverlayEdits.SetSize(before, index, to) : null;
        var after = resized is { Refused: null } ? actions.Settle(before, resized.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is { } change)
        {
            settings.Edit(change);
            actions.Gestures?.Add(GestureKind.Resize, before, after, joins: true);
            actions.RequestPaint();
        }
    }

    /// <summary>Shows the resizing cursor on a floating overlay's corner; false elsewhere, where the class's cursor shows.</summary>
    public bool CornerCursor(OverlayConfig overlay, PaintedOverlay? painted, Position at)
    {
        if (!InCorner(overlay, painted, at, actions.Cursor()))
        {
            return false;
        }

        PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_SIZENWSE));
        return true;
    }

    /// <summary>
    /// Makes a floating overlay's resizing corner take a press across its whole square: a layered window lets clicks through
    /// its fully transparent pixels, which a round pill leaves there, so they get an alpha of 1/255, which shows nothing.
    /// </summary>
    public static void HoldCorner(Bitmap bitmap, OverlayConfig overlay, float scale)
    {
        if (!Resizable(overlay))
        {
            return;
        }

        var side = (int)Math.Ceiling(CornerSize * scale);
        var unseen = Color.FromArgb(1, 0, 0, 0);
        for (var y = Math.Max(0, bitmap.Height - side); y < bitmap.Height; y++)
        {
            for (var x = Math.Max(0, bitmap.Width - side); x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0)
                {
                    bitmap.SetPixel(x, y, unseen);
                }
            }
        }
    }

    private static bool Resizable(OverlayConfig overlay) => overlay.Snapped is null && !overlay.Locked;

    /// <summary>Whether <paramref name="cursor"/> is on the resizing corner of a floating overlay at <paramref name="at"/>.</summary>
    public static bool InCorner(OverlayConfig overlay, PaintedOverlay? painted, Position at, Position cursor)
    {
        if (!Resizable(overlay) || painted is null)
        {
            return false;
        }

        var side = CornerSize * painted.Scale;
        var (x, y) = (cursor.X - at.X, cursor.Y - at.Y);
        return x >= painted.Size.Width - side && y >= painted.Size.Height - side && x < painted.Size.Width && y < painted.Size.Height;
    }

    /// <summary>
    /// "Separate" from the overlay's menu, as one gesture: the pill lands as the overlay a drag of it carries, at its size,
    /// beside the overlay as the rest of its items will draw it; nothing before the overlay first painted.
    /// </summary>
    public void Separate(string pill, PaintedOverlay? painted, IReadOnlyList<Screen> screens)
    {
        if (painted is null)
        {
            return;
        }

        PInvoke.GetWindowRect(hwnd, out var rect);
        var shown = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
        Size made;
        using (var picture = PillPicture(pill, painted))
        {
            made = picture.Size;
        }

        var ids = DesktopGestures.PillItems(settings.Config, pill);
        Size rest;
        using (var painter = new Painter())
        {
            rest = painter.Measure([.. painted.Items.Where(each => !ids.Contains(each.Id))], painted.Look, painted.Scale);
        }

        var before = settings.Config;
        var drop = DesktopGestures.Separate(before, index, pill, shown, (made.Width, made.Height), (rest.Width, rest.Height), screens);
        var after = drop.Result.Refused is null ? actions.Settle(before, drop.Result.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is { } change)
        {
            settings.Edit(change);
            actions.Gestures?.Add(drop.Kind, before, after, OverlayText.UndoSeparate);
            actions.RequestPaint();
        }
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
        return Picture([.. painted.Items.Where(each => ids.Contains(each.Id))], painted);
    }

    // Off its pill, an item shows its pill's label, as the overlay it becomes does.
    private Bitmap ItemPicture(string item, PaintedOverlay painted)
    {
        var ids = DesktopGestures.PillItems(settings.Config, item);
        var label = painted.Items.FirstOrDefault(each => ids.Contains(each.Id) && each.Label is not null)?.Label;
        return Picture([.. painted.Items.Where(each => each.Id == item).Select(each => each with { Label = label })], painted);
    }

    // Carried over other pills, it keeps a background of its own whatever the look's opacity or the window's fade.
    private static Bitmap Picture(IReadOnlyList<OverlayItem> items, PaintedOverlay painted)
    {
        using var painter = new Painter();
        return painter.Paint(items, painted.Look with { Orientation = Orientation.Horizontal, Background = 1, Opacity = 1 }, painted.Scale);
    }
}
