using System.Drawing;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.App.Gestures;

/// <summary>What a press on an overlay reaches of its OS and of the other overlays, which each host gives its own way.</summary>
internal interface IPressHost
{
    TimeProvider Time { get; }

    /// <summary>The pointer, in the screen's pixels.</summary>
    Position Cursor();

    void RequestPaint();

    /// <summary>The overlay a drag is over now, and where a drop would join it, which that overlay marks.</summary>
    DropPreview Preview { get; }

    /// <summary>Each overlay's box on the screen, by its place, null for one not shown.</summary>
    IReadOnlyList<PixelRect?> Shown();

    /// <summary>Where a drop at a point of the screen joins an overlay, as its painted places say.</summary>
    DropTarget JoinAt(int onto, Position cursor, string? alone);

    /// <summary>A gesture's settings, with the overlays it pushed along a taskbar keeping their places, or why it is refused.</summary>
    EditResult Settle(ThermiConfig before, ThermiConfig after);

    /// <summary>The last desktop gesture, which the overlays' menus offer to undo; none where the host offers no undo.</summary>
    GestureUndo? Gestures { get; }

    /// <summary>Wakes <see cref="OverlayPress.Hold"/> after <paramref name="after"/>, in place of a wake asked before.</summary>
    void Wake(TimeSpan after);

    /// <summary>No wake is owed any more.</summary>
    void Sleep();

    /// <summary>Takes Esc while the press holds, so it cancels the drag and reaches no other program; false where it cannot.</summary>
    bool HoldEscape();

    /// <summary>Lets go of Esc, taken by <see cref="HoldEscape"/>.</summary>
    void LetEscape();

    /// <summary>The overlay follows the drag, its corner to <paramref name="at"/>.</summary>
    void MoveTo(Position at);
}

/// <summary>The pointer's shape over an overlay, which each desktop shows with its own cursor of that name.</summary>
internal enum PointerShape
{
    Default,

    /// <summary>A drop here does nothing: at the cap of overlays, or on a taskbar with no room.</summary>
    NotAllowed,

    /// <summary>On a floating overlay's corner, or dragging it: the arrows from top-left to bottom-right.</summary>
    Resize,
}

/// <summary>
/// An overlay's desktop gestures on every OS: the overlay moved onto a snap mark or into another overlay, a pill or an item
/// held still until it lifts and then dragged off, a floating overlay's corner, and Ctrl and the wheel. The host says
/// where the pointer is and what the overlay drew; these decide what a press does.
/// </summary>
internal sealed class OverlayPress(int index, Settings settings, IPressHost host) : IDisposable
{
    /// <summary>The corner of a floating overlay that resizes it, in pixels at 100 %, square.</summary>
    public const float CornerSize = 14;

    // When the press began, on the injected clock, which says whether it held long enough.
    private long _pressed;

    // Whether Esc is held for the press now, which it lets go with the press.
    private bool _escape;

    // What the wheel's turns left short of a notch, which the next turn adds to.
    private int _turned;

    /// <summary>The drag a press holds, which the overlay is painted as, or null.</summary>
    public DesktopDrag? Drag { get; private set; }

    public void Dispose() => Let();

    /// <summary>Where the overlay goes while a press holds it: where the drag has it, unless it carries a pill off or holds the corner.</summary>
    public Position? HeldAt(Position? dragged) => Drag?.Moves == false ? null : dragged;

    /// <summary>Whether a drag holds the overlay or is over it, which keeps it whole, without its fade.</summary>
    public bool Holds => Drag is not null || host.Preview.Onto == index;

    // A lifted pill leaves its overlay in place, which a drop back on it joins as any other.
    private int Self => Drag?.Lifted == true ? -1 : index;

    /// <summary>
    /// A press on <paramref name="overlay"/> at <paramref name="at"/>, drawn <paramref name="size"/> big at
    /// <paramref name="scale"/>: on its corner, its edge, or the item it is on, in its pill and its own place, on its pill's
    /// label or not. On a pill it starts the wait for the hold that lifts the pill, or the item alone as Core's
    /// <see cref="DesktopGestures.Grip"/> says; <paramref name="show"/> shows what the drag carries.
    /// </summary>
    public void Press(OverlayConfig overlay, Size size, float scale, IReadOnlyList<Screen> screens, PressedOn on, Position at, Func<Carried?, IDragShow> show)
    {
        Let();
        var carried = DesktopGestures.Grip(settings.Config, on.Item, on.OnLabel);
        var held = InCorner(overlay, size, scale, at, host.Cursor()) ? Held.Corner : carried is null ? Held.Edge : Held.Pill;
        var (box, gap) = carried is { Alone: true } ? (Rect(on.Place), Rect(on.Alone)) : (Rect(on.Pill), Rect(on.Pill));
        var start = new DragStart(held, carried, box, gap, at, size, scale);
        Drag = new DesktopDrag(index, settings, start, screens, show(carried), host.Settle);
        _pressed = host.Time.GetTimestamp();
        _escape = host.HoldEscape();
        if (held == Held.Pill)
        {
            host.Wake(DesktopGestures.Hold);
        }
    }

    /// <summary>The hold's wake: the pill lifts once the clock says the press held long enough, or it waits on.</summary>
    public void Hold()
    {
        host.Sleep();
        var held = host.Time.GetElapsedTime(_pressed);
        if (Drag is null)
        {
            return;
        }

        if (held < DesktopGestures.Hold)
        {
            host.Wake(DesktopGestures.Hold - held);
            return;
        }

        Drag.Lift(held);
        if (Drag.Lifted)
        {
            host.RequestPaint();
        }
    }

    /// <summary>The pointer dragged to <paramref name="to"/>: the overlay follows unless the press carries a pill or holds the corner.</summary>
    public void Move(Position to, Position? dragged)
    {
        if (Drag?.Moves != false)
        {
            host.MoveTo(to);
        }

        var target = Drag is null or { Resizes: true } or { Refused: true } ? default : Target(host.Cursor(), dragged);
        var moved = Drag?.Moved(dragged, target.Onto is not null) == true;
        // The overlay a drop would join marks where, which it paints.
        var previewed = host.Preview.Over(target);
        if (previewed || moved)
        {
            host.RequestPaint();
        }
    }

    /// <summary>A drag dropped onto an overlay joins it; the corner's preview ends with the press either way. True when it wrote the settings.</summary>
    public bool Release(Press press, DragDistance distance)
    {
        var cursor = host.Cursor();
        var written = Drag?.Release(press, cursor, distance, Target(cursor, Drag.Dropped), host.Gestures) == true;
        Let();
        return written;
    }

    // An item carried alone may land within its own pill, which only its own overlay can tell.
    private DropTarget Target(Position cursor, Position? dragged) =>
        dragged is { } at && Drag is { } drag
        && DesktopGestures.JoinTarget(host.Shown(), Self, drag.Box(at), cursor, drag.AltHeld()) is var (onto, point)
            ? host.JoinAt(onto, point, drag is { Lifted: true, Carried: { Alone: true } alone } && onto == index ? alone.Item : null)
            : default;

    /// <summary>
    /// Esc, or another grab taking the pointer from the press, as the overview or a dialog does: the drag ends where it
    /// began, writing nothing, and the overlay goes back. False when no press held it.
    /// </summary>
    public bool Cancel()
    {
        if (Drag is not { } drag)
        {
            return false;
        }

        var origin = drag.Origin;
        Let();
        host.MoveTo(origin);
        host.RequestPaint();
        return true;
    }

    /// <summary>
    /// The pointer's shape over the overlay at <paramref name="at"/>, drawn <paramref name="size"/> big, as Windows shows
    /// it: no entry while a drop would do nothing, the resize arrows while the press holds the corner of a floating overlay
    /// or the pointer rests on it, and else the desktop's own.
    /// </summary>
    public PointerShape Shape(OverlayConfig overlay, Size? size, float scale, Position at) => Drag switch
    {
        { Refuses: true } => PointerShape.NotAllowed,
        { Resizes: true } => PointerShape.Resize,
        null when size is { } drawn && InCorner(overlay, drawn, scale, at, host.Cursor()) => PointerShape.Resize,
        _ => PointerShape.Default,
    };

    /// <summary>Ends the press's drag, whatever it did.</summary>
    public void Let()
    {
        host.Sleep();
        if (_escape)
        {
            host.LetEscape();
            _escape = false;
        }

        Drag?.Dispose();
        Drag = null;
        if (host.Preview.Over(default))
        {
            host.RequestPaint();
        }
    }

    /// <summary>A double-click on the corner, which sets the overlay back to its own size; false anywhere else.</summary>
    public bool DoubleClickedCorner(OverlayConfig overlay, Size? size, float scale, Position at)
    {
        if (size is not { } drawn || !InCorner(overlay, drawn, scale, at, host.Cursor()))
        {
            return false;
        }

        var before = settings.Config;
        var resized = OverlayEdits.Resize(before, index, 1, overlay.Style.Orientation);
        var after = resized.Refused is null ? host.Settle(before, resized.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is { } change)
        {
            settings.Edit(change);
            host.Gestures?.Add(GestureKind.Resize, before, after);
        }

        return true;
    }

    /// <summary>
    /// The wheel turned by <paramref name="delta"/>, <see cref="OverlaySizes.Notch"/> a notch, over the overlay: with
    /// <paramref name="control"/> held, it takes the size Core's steps give, a notch within a second of the last joining
    /// its step, and true. A locked overlay keeps its size, as it keeps its corner, and so does one a press holds.
    /// </summary>
    public bool Wheel(bool control, int delta, IReadOnlyList<Screen> screens)
    {
        var before = settings.Config;
        if (!control)
        {
            return false;
        }

        if (Drag is null && index < before.Overlays.Count && !before.Overlays[index].Locked)
        {
            Resize(before, delta, screens);
        }

        return true;
    }

    private void Resize(ThermiConfig before, int delta, IReadOnlyList<Screen> screens)
    {
        (var size, _turned) = OverlaySizes.Wheel(before.Overlays[index], screens, _turned, delta);
        var resized = size is { } to ? OverlayEdits.SetSize(before, index, to) : null;
        var after = resized is { Refused: null } ? host.Settle(before, resized.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is { } change)
        {
            settings.Edit(change);
            host.Gestures?.Add(GestureKind.Resize, before, after, joins: true);
            host.RequestPaint();
        }
    }

    /// <summary>Whether a floating overlay takes a resize by its corner: a snapped or locked one keeps its size.</summary>
    public static bool Resizable(OverlayConfig overlay) => overlay.Snapped is null && !overlay.Locked;

    /// <summary>
    /// Whether <paramref name="cursor"/> is on the resizing corner of a floating overlay at <paramref name="at"/>, drawn
    /// <paramref name="size"/> big.
    /// </summary>
    public static bool InCorner(OverlayConfig overlay, Size size, float scale, Position at, Position cursor)
    {
        if (!Resizable(overlay))
        {
            return false;
        }

        var side = CornerSize * scale;
        var (x, y) = (cursor.X - at.X, cursor.Y - at.Y);
        return x >= size.Width - side && y >= size.Height - side && x < size.Width && y < size.Height;
    }

    private static RectangleF Rect(PillBox box) => new((float)box.Left, (float)box.Top, (float)box.Width, (float)box.Height);
}
