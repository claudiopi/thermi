using System.Drawing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Gestures;
using Thermi.Painting;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Overlay;

/// <summary>The overlay's desktop gestures: the overlay moved onto a snap mark or into another overlay, a pill dragged off with Ctrl, and a floating overlay's corner.</summary>
internal sealed partial class OverlayWindow
{
    // The corner of a floating overlay that resizes it, in pixels at 100 %, square.
    private const float CornerSize = 14;

    private DesktopDrag? _drag;

    // What the last paint drew, which a pill carried off is painted from.
    private (IReadOnlyList<OverlayItem> Items, OverlayLook Look, float Scale, Size Size)? _painted;

    // Where the overlay goes while a press holds it: where the drag has it, unless it carries a pill off or holds the corner.
    private Position? HeldAt => _drag?.Moves == false ? null : _mouse.Dragged;

    private void Pressed(OverlayConfig overlay)
    {
        PInvoke.SetCapture(Hwnd);
        _tips.Quiet(true);
        _drag?.Dispose();
        _drag = null;
        if (_painted is not { } painted || _scene is not { } scene)
        {
            return;
        }

        var cursor = _actions.Cursor();
        var item = ItemAt(cursor);
        var held = InCorner(overlay, cursor) ? Held.Corner : item is null ? Held.Edge : Held.Pill;
        var box = item is null ? RectangleF.Empty : _painter.Places.First(place => place.Id == item).Pill;
        // The key's state as it was when the press was sent, which a posted press without a keyboard reads as up.
        var ctrl = PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0;
        var start = new DragStart(held, item, ctrl, box, WindowPosition(), painted.Size, painted.Scale, item is null ? null : () => PillPicture(item, painted));
        _drag = new DesktopDrag(_index, _settings, start, scene.Screens, Hwnd);
    }

    private void Dragging(MouseStep step)
    {
        if (_drag?.Moves != false)
        {
            PInvoke.SetWindowPos(Hwnd, HWND.Null, step.At!.X, step.At.Y, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }

        if (_drag?.Moved(_mouse.Dragged) == true)
        {
            _actions.RequestPaint();
        }
    }

    // A drag dropped onto another overlay merges into it; the corner's preview ends with the press either way.
    private void Released(MouseStep step)
    {
        var cursor = _actions.Cursor();
        _drag?.Release(step.Press!, cursor, DragDistance(), _actions.OverlayAt(_index, cursor), _actions.Gestures, Translations);
        Dropped();
    }

    private void Dropped()
    {
        _tips.Quiet(false);
        _drag?.Dispose();
        _drag = null;
        _actions.RequestPaint();
    }

    private bool DoubleClickedCorner(OverlayConfig overlay)
    {
        if (!InCorner(overlay, _actions.Cursor()))
        {
            return false;
        }

        var before = _settings.Config;
        var after = OverlayEdits.Resize(before, _index, 1, overlay.Style.Orientation);
        if (after.Refused is null && ConfigEdits.Replay(before, after.Config) is { } change)
        {
            _settings.Edit(change);
            _actions.Gestures?.Add(GestureKind.Resize, before, after.Config);
        }

        return true;
    }

    private bool InCorner(OverlayConfig overlay, Position cursor)
    {
        if (overlay.Snapped is not null || overlay.Locked || _painted is not { } painted)
        {
            return false;
        }

        var corner = WindowPosition();
        var side = CornerSize * painted.Scale;
        return cursor.X - corner.X >= painted.Size.Width - side && cursor.Y - corner.Y >= painted.Size.Height - side
            && cursor.X - corner.X < painted.Size.Width && cursor.Y - corner.Y < painted.Size.Height;
    }

    private bool CornerCursor(OverlayConfig overlay)
    {
        if (!InCorner(overlay, _actions.Cursor()))
        {
            return false;
        }

        PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_SIZENWSE));
        return true;
    }

    private Bitmap PillPicture(string item, (IReadOnlyList<OverlayItem> Items, OverlayLook Look, float Scale, Size Size) painted)
    {
        var ids = DesktopGestures.PillItems(_settings.Config, item);
        // Carried over other pills, it keeps a background of its own whatever the look's opacity or the window's fade.
        using var painter = new Painter();
        return painter.Paint([.. painted.Items.Where(each => ids.Contains(each.Id))], painted.Look with { Orientation = Orientation.Horizontal, Background = 1, Opacity = 1 }, painted.Scale);
    }

    /// <summary>
    /// The overlay as its last paint drew it, painted afresh without what only the open window or a broken file adds; the
    /// "Sensors stopped" pill while they are. Null before the first paint.
    /// </summary>
    internal Bitmap? Picture()
    {
        if (_painted is not { } painted || _scene is not { } scene)
        {
            return null;
        }

        using var painter = new Painter();
        var texts = Translations;
        IReadOnlyList<OverlayItem> items = [.. painted.Items.Where(item => item.Id.Length > 0)];
        return scene.Service == Core.Tray.ServiceState.Stopped || items.Count == 0
            ? painter.PaintStopped(texts.Of(Core.Translations.OverlayText.SensorsStopped), painted.Look, painted.Scale)
            : painter.Paint(items, painted.Look, painted.Scale);
    }

    internal void UndoGesture()
    {
        if (_actions.Gestures?.Take() is { } undo)
        {
            _settings.Edit(undo);
            _actions.RequestPaint();
        }
    }
}
