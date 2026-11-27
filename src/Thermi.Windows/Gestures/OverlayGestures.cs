using System.Drawing;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Painting;
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
/// An overlay's desktop gestures on Windows: Core's and <see cref="OverlayPress"/>'s decisions, with the hold's timer, Esc
/// as the overlay's hotkey while pressed, the window moved, the pill carried off painted by GDI+, and the corner's cursor.
/// </summary>
internal sealed class OverlayGestures : IPressHost, IDisposable
{
    /// <summary>The timer that wakes the overlay when a press on a pill may have held long enough to lift it or its item.</summary>
    internal const nuint HoldTimer = 3;

    /// <summary>The id of Esc, which a press holds as a hotkey of the overlay's, so it cancels the drag and reaches no other app.</summary>
    internal const nuint EscapeKey = 1;

    private readonly int _index;
    private readonly Settings _settings;
    private readonly OverlayActions _actions;
    private readonly HWND _hwnd;
    private readonly OverlayPress _press;

    public OverlayGestures(int index, Settings settings, OverlayActions actions, HWND hwnd)
    {
        (_index, _settings, _actions, _hwnd) = (index, settings, actions, hwnd);
        _press = new OverlayPress(index, settings, this);
    }

    /// <summary>The drag a press holds, which the overlay is painted as, or null.</summary>
    public DesktopDrag? Drag => _press.Drag;

    public TimeProvider Time => _actions.Time;

    public DropPreview Preview => _actions.Preview;

    public GestureUndo? Gestures => _actions.Gestures;

    public void Dispose() => _press.Dispose();

    /// <summary>Where the overlay goes while a press holds it: where the drag has it, unless it carries a pill off or holds the corner.</summary>
    public Position? HeldAt(Position? dragged) => _press.HeldAt(dragged);

    /// <summary>Whether a drag holds the overlay or is over it, which keeps it whole, without its fade.</summary>
    public bool Holds => _press.Holds;

    /// <summary>A press on <paramref name="overlay"/> at <paramref name="at"/>, on the item it is on, or its edge or corner.</summary>
    public void Press(OverlayConfig overlay, PaintedOverlay painted, IReadOnlyList<Screen> screens, PressedOn on, Position at) =>
        _press.Press(overlay, painted.Size, painted.Scale, screens, on, at, carried => new WindowDragShow(_hwnd, painted.Scale, carried switch
        {
            { Alone: true } alone => () => ItemPicture(alone.Item, painted),
            { } pill => () => PillPicture(pill.Item, painted),
            _ => null,
        }));

    /// <summary>The hold's timer woke the overlay: the pill lifts once the clock says the press held long enough, or it waits on.</summary>
    public void Hold() => _press.Hold();

    /// <summary>The pointer dragged to <paramref name="to"/>: the overlay follows unless the press carries a pill or holds the corner.</summary>
    public void Move(Position to, Position? dragged) => _press.Move(to, dragged);

    /// <summary>A drag dropped onto an overlay joins it; the corner's preview ends with the press either way.</summary>
    public void Release(Press press, DragDistance distance) => _press.Release(press, distance);

    /// <summary>
    /// Whether Esc was down as the message being handled was sent, which cancels a drag: read as the mouse moves for when
    /// another app holds Esc as its hotkey.
    /// </summary>
    public static bool Escape() => PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_ESCAPE) < 0;

    /// <summary>Lets the mouse go after Esc cancelled the press; the change of capture that follows finds no press.</summary>
    public void LetMouseGo()
    {
        if (PInvoke.GetCapture() == _hwnd)
        {
            PInvoke.ReleaseCapture();
        }
    }

    /// <summary>Ends the press's drag, whatever it did.</summary>
    public void Let() => _press.Let();

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

        if (_actions.Preview.Onto == _index)
        {
            var within = _actions.Preview.InPill ? Drag?.Carried?.Item : null;
            DragMarks.RingAndBar(bitmap, places, (_actions.Preview.Before, within), vertical, accent, scale);
        }
    }

    /// <summary>A double-click on the corner, which sets the overlay back to its own size; false anywhere else.</summary>
    public bool DoubleClickedCorner(OverlayConfig overlay, PaintedOverlay? painted, Position at) =>
        _press.DoubleClickedCorner(overlay, painted?.Size, painted?.Scale ?? 1, at);

    /// <summary>
    /// The wheel turned over the overlay, as <c>WM_MOUSEWHEEL</c>'s <paramref name="wParam"/> says: with Ctrl, the overlay
    /// takes the size Core's steps give, and true.
    /// </summary>
    public bool Wheel(nuint wParam, IReadOnlyList<Screen> screens) =>
        _press.Wheel((wParam & (nuint)MODIFIERKEYS_FLAGS.MK_CONTROL) != 0, (short)(wParam >> 16), screens);

    /// <summary>Shows the resizing cursor on a floating overlay's corner; false elsewhere, where the class's cursor shows.</summary>
    public bool CornerCursor(OverlayConfig overlay, PaintedOverlay? painted, Position at)
    {
        if (!InCorner(overlay, painted, at, _actions.Cursor()))
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
        if (!OverlayPress.Resizable(overlay))
        {
            return;
        }

        var side = (int)Math.Ceiling(OverlayPress.CornerSize * scale);
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

    /// <summary>Whether <paramref name="cursor"/> is on the resizing corner of a floating overlay at <paramref name="at"/>.</summary>
    public static bool InCorner(OverlayConfig overlay, PaintedOverlay? painted, Position at, Position cursor) =>
        painted is not null && OverlayPress.InCorner(overlay, painted.Size, painted.Scale, at, cursor);

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

        PInvoke.GetWindowRect(_hwnd, out var rect);
        var shown = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
        Size made;
        using (var picture = PillPicture(pill, painted))
        {
            made = picture.Size;
        }

        var ids = DesktopGestures.PillItems(_settings.Config, pill);
        Size rest;
        using (var painter = new Painter())
        {
            rest = painter.Measure([.. painted.Items.Where(each => !ids.Contains(each.Id))], painted.Look, painted.Scale);
        }

        var drop = DesktopGestures.Separate(_settings.Config, _index, pill, shown, (made.Width, made.Height), (rest.Width, rest.Height), screens);
        if (DropWrite.Write(_settings, _index, drop, _actions.Settle, move: null, _actions.Gestures, OverlayText.UndoSeparate))
        {
            _actions.RequestPaint();
        }
    }

    /// <summary>Takes back the last desktop gesture, as the overlay's menu offers.</summary>
    public void Undo()
    {
        if (_actions.Gestures?.Take() is { } undo)
        {
            _settings.Edit(undo);
            _actions.RequestPaint();
        }
    }

    Position IPressHost.Cursor() => _actions.Cursor();

    void IPressHost.RequestPaint() => _actions.RequestPaint();

    IReadOnlyList<PixelRect?> IPressHost.Shown() => _actions.Shown();

    DropTarget IPressHost.JoinAt(int onto, Position cursor, string? alone) => _actions.JoinAt(onto, cursor, alone);

    EditResult IPressHost.Settle(ThermiConfig before, ThermiConfig after) => _actions.Settle(before, after);

    void IPressHost.Wake(TimeSpan after) => PInvoke.SetTimer(_hwnd, HoldTimer, (uint)Math.Ceiling(after.TotalMilliseconds), null);

    void IPressHost.Sleep() => PInvoke.KillTimer(_hwnd, HoldTimer);

    bool IPressHost.HoldEscape() => PInvoke.RegisterHotKey(_hwnd, (int)EscapeKey, HOT_KEY_MODIFIERS.MOD_NOREPEAT, (uint)VIRTUAL_KEY.VK_ESCAPE);

    void IPressHost.LetEscape() => PInvoke.UnregisterHotKey(_hwnd, (int)EscapeKey);

    void IPressHost.MoveTo(Position at) =>
        PInvoke.SetWindowPos(
            _hwnd,
            HWND.Null,
            at.X,
            at.Y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    private Bitmap PillPicture(string item, PaintedOverlay painted)
    {
        var ids = DesktopGestures.PillItems(_settings.Config, item);
        return Picture([.. painted.Items.Where(each => ids.Contains(each.Id))], painted);
    }

    // Off its pill, an item shows its pill's label, as the overlay it becomes does.
    private Bitmap ItemPicture(string item, PaintedOverlay painted)
    {
        var ids = DesktopGestures.PillItems(_settings.Config, item);
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
