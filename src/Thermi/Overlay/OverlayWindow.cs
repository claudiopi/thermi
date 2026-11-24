using System.Drawing;
using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Gestures;
using Thermi.Painting;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using Select = Thermi.Core.Messages.Select;
using SelectHow = Thermi.Core.Messages.SelectHow;

namespace Thermi.Overlay;

/// <summary>
/// One overlay of the settings, found by its place in them: a layered window that only draws, with its own painter, its
/// own items' history, its menu and the mouse, whose presses its gestures decide, and its fade. The host runs its messages.
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    internal const string ClassName = "thermi-overlay";
    internal const WINDOW_EX_STYLE ExStyle =
        WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

    // A glow's frames come about 30 times a second, from a timer of the overlay's own, only while it glows.
    private const nuint GlowTimer = 1;
    private const uint GlowFrame = 33;

    private readonly int _index;
    private readonly Settings _settings;
    private readonly OverlayActions _actions;
    private readonly Painter _painter = new(reuse: true);
    private readonly LayeredPicture _picture = new();
    private readonly OverlayMouse _mouse = new();
    private readonly Topmost _topmost;
    private readonly ValueTips _tips;
    private readonly CriticalGlow _glow;
    private readonly OverlayFade _fade;
    private readonly OverlayGestures _gestures;

    // OLED care moves the overlay by how long it has run.
    private readonly long _started;
    private bool _shown;

    // The last scene painted, which the glow's frames paint again, and what it drew.
    private OverlayScene? _scene;
    private PaintedOverlay? _painted;

    // The item the pointer is on, which the open window marks on its row.
    private string? _pointed;

    public OverlayWindow(int index, Settings settings, OverlayActions actions)
    {
        _index = index;
        _settings = settings;
        _actions = actions;
        _glow = new CriticalGlow(actions.Time);
        _started = actions.Time.GetTimestamp();
        Hwnd = HiddenWindow.Create(ClassName, ExStyle);
        _topmost = new Topmost(Hwnd);
        _tips = new ValueTips(Hwnd);
        _fade = new OverlayFade(Hwnd, actions.Cursor);
        _gestures = new OverlayGestures(index, settings, actions, Hwnd);
    }

    public HWND Hwnd { get; }

    /// <summary>What the open window marks on this overlay at the next paint.</summary>
    public EditMarks Marks { get; set; } = EditMarks.None;

    // A deleted overlay's window lives until the next paint lets it go.
    private OverlayConfig? Overlay => _index < _settings.Config.Overlays.Count ? _settings.Config.Overlays[_index] : null;

    // The display language is read once, at start; the settings' language whenever they reload.
    private Texts Translations => Texts.For(_settings.Config.Language, CultureInfo.CurrentUICulture);

    // Without a class cursor Windows leaves whatever cursor was last shown, often the busy one. Without CS_DBLCLKS
    // a double-click comes as a second press, and never opens the settings.
    internal static WNDCLASSEXW WindowClass(WNDPROC procedure, PCWSTR className) => HiddenWindow.Plain(procedure, className) with
    {
        style = WNDCLASS_STYLES.CS_DBLCLKS,
        hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
    };

    /// <summary>
    /// The scale the overlay is drawn at, times its own size: the monitor's it is about to show on among
    /// <paramref name="screens"/>, so a snap to a monitor of another scale paints right at once, or its window's,
    /// <paramref name="dpi"/>, while it knows none.
    /// </summary>
    internal static float ScaleOf(uint dpi, OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        (float)((Snaps.ScreenOf(overlay, screens) is { } screen ? screen.Scale : dpi / 96.0) * overlay.Size);

    /// <summary>Puts a picture shown once, as a dragged pill's, on screen at <paramref name="position"/>.</summary>
    internal static void Show(HWND hwnd, Bitmap bitmap, Position position)
    {
        using var picture = new LayeredPicture();
        picture.Show(hwnd, bitmap, position);
    }

    public void Dispose()
    {
        _picture.Dispose();
        _topmost.Dispose();
        _tips.Dispose();
        _gestures.Dispose();
        _fade.Dispose();
        PInvoke.KillTimer(Hwnd, GlowTimer);
        PInvoke.DestroyWindow(Hwnd);
        _painter.Dispose();
    }

    public void Raise()
    {
        if (_shown)
        {
            _topmost.Raise();
        }
    }

    /// <summary>Takes the overlay off the screen until a paint shows it again; it keeps its history meanwhile.</summary>
    public void Hide()
    {
        if (_shown)
        {
            PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
            _shown = false;
        }
    }

    /// <summary>True when the message was the overlay's to handle.</summary>
    public bool Handle(uint message, nuint wParam)
    {
        if (Overlay is not { } overlay)
        {
            return false;
        }

        switch (message)
        {
            case PInvoke.WM_DPICHANGED:
                _actions.RequestPaint();
                return true;
            case PInvoke.WM_LBUTTONDOWN or PInvoke.WM_MOUSEMOVE or PInvoke.WM_LBUTTONUP or PInvoke.WM_CAPTURECHANGED or PInvoke.WM_LBUTTONDBLCLK:
                return Mouse(message, overlay);
            case PInvoke.WM_RBUTTONUP:
                ShowMenu();
                return true;
            case PInvoke.WM_MOUSELEAVE:
                Point(null);
                return true;
            case PInvoke.WM_SETCURSOR:
                return CornerCursor(overlay);
            case PInvoke.WM_TIMER when wParam == OverlayFade.TimerId:
                _fade.Now(overlay, _painted?.Scale, _scene?.WindowOpen == true, _gestures.Drag is not null, _shown);
                return true;
            case PInvoke.WM_TIMER when _shown && _scene is { } scene && _index < scene.Config.Overlays.Count:
                Repaint(scene);
                return true;
            default:
                return false;
        }
    }

    public void Repaint(OverlayScene scene)
    {
        _scene = scene;
        var overlay = _gestures.Drag?.Shown(scene.Config.Overlays[_index]) ?? scene.Config.Overlays[_index];
        var heldAt = _gestures.HeldAt(_mouse.Dragged);
        var texts = Texts.For(scene.Config.Language, CultureInfo.CurrentUICulture);
        var input = InputOf(scene, texts);
        var shown = ThermiMark.Overlay(scene.Config, _index, input, scene.OpenedOnce);
        var marks = Marks;
        ClickThrough(overlay.ClickThrough);
        var look = marks.Applied(OverlayLook.Of(overlay.Style, scene.LightTaskbar, scene.HighContrast), held: heldAt is not null);
        // A pill held in a drag goes where the pointer takes it, whose monitor's scale the window gets as it crosses.
        var scale = ScaleOf(PInvoke.GetDpiForWindow(Hwnd), overlay, heldAt is null ? scene.Screens : []);
        var items = Extras(scene, texts, shown.Items);
        _glow.Update(items, scene.Animate);
#pragma warning disable CA2000 // The painter reuses the bitmap while its size holds, and disposes it itself.
        var bitmap = shown.SensorsStopped
            ? _painter.PaintStopped(texts.Of(OverlayText.SensorsStopped), look, scale, items)
            : _painter.Paint(items, look, scale, _glow.Glowing ? _glow.Of : null);
#pragma warning restore CA2000
        marks.Draw(bitmap, shown.SensorsStopped ? [] : _painter.Places, items, scale);
        Tips(scene, input, shown);
        Glow();
        _painted = new PaintedOverlay(items, look, scale, bitmap.Size) { Stopped = shown.SensorsStopped };
        var running = _actions.Time.GetElapsedTime(_started);
        var row = scene.Row(_index, () => shown.SensorsStopped ? bitmap.Width : TaskbarRoom.Width(_painter, items, look, scale, bitmap.Width),
            _actions.RequestPaint);
        var at = heldAt ?? Snaps.Locate(overlay, scene.Screens, bitmap.Width, bitmap.Height, running, row) ?? WindowPosition();
        _picture.Show(Hwnd, bitmap, at, _fade.Alpha);
        _fade.Watch(overlay, scale, scene.WindowOpen, _gestures.Drag is not null, _shown);
        if (!_shown)
        {
            const SET_WINDOW_POS_FLAGS Shown = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
            PInvoke.SetWindowPos(Hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, Shown);
            _shown = true;
        }
    }

    private TrayInput InputOf(OverlayScene scene, Texts texts) =>
        new(scene.Snapshot, scene.Service, _actions.Culture, texts, scene.LightTaskbar, "")
        {
            History = _actions.History,
            WindowOpen = scene.WindowOpen,
            BaseClock = scene.Cpu?.BaseClock,
            Busiest = scene.Cpu?.Busiest,
        };

    // After the items, the settings' error.
    private static IReadOnlyList<OverlayItem> Extras(OverlayScene scene, Texts texts, IReadOnlyList<OverlayItem> items)
    {
        if (scene.Problem is null)
        {
            return items;
        }

        var error = texts.Of(OverlayText.ConfigError);
        return [.. items, new OverlayItem(texts.Of(OverlayText.ConfigLabel), error, Level.Critical, error)];
    }

    // Each value's tooltip where the painter drew its item, the "Sensors stopped" pill's over the whole pill.
    private void Tips(OverlayScene scene, TrayInput input, Core.Tray.OverlayScene shown)
    {
        _tips.Theme(!scene.LightApps && scene.HighContrast is null);
        _tips.Update([.. _painter.Places.Zip(shown.Tooltips, (place, tip) => (place.Item, ValueTooltips.Text(scene.Config, _index, input, tip)))]);
    }

    private void Glow()
    {
        if (_glow.Glowing)
        {
            PInvoke.SetTimer(Hwnd, GlowTimer, GlowFrame, null);
        }
        else
        {
            PInvoke.KillTimer(Hwnd, GlowTimer);
        }
    }

    private bool Mouse(uint message, OverlayConfig overlay)
    {
        var step = _mouse.On(message, _actions.Cursor(), WindowPosition(), DragDistance(), overlay.Locked);
        switch (step.Action)
        {
            case MouseAction.Capture:
                Pressed(overlay);
                return true;
            case MouseAction.Move:
                _gestures.Move(step.At!, _mouse.Dragged);
                return true;
            case MouseAction.Release:
                PInvoke.ReleaseCapture();
                Clicked(step.Press!, overlay.Locked);
                _gestures.Release(step.Press!, DragDistance());
                Dropped();
                return true;
            case MouseAction.Lost:
                Dropped();
                return true;
            case MouseAction.Open when !_gestures.DoubleClickedCorner(overlay, _painted, WindowPosition()):
                _actions.OpenSettings(_index, ItemAt(_actions.Cursor()));
                return true;
            case MouseAction.Open:
                return true;
            case MouseAction.None when message == PInvoke.WM_MOUSEMOVE:
                Point(ItemAt(_actions.Cursor()));
                return false;
            case MouseAction.None:
            default:
                return false;
        }
    }

    private void Pressed(OverlayConfig overlay)
    {
        PInvoke.SetCapture(Hwnd);
        _tips.Quiet(true);
        _gestures.Let();
        if (_painted is { } painted && _scene is { } scene)
        {
            var item = ItemAt(_actions.Cursor());
            var box = item is null ? RectangleF.Empty : _painter.Places.First(place => place.Id == item).Pill;
            _gestures.Press(overlay, painted, scene.Screens, (item, box), WindowPosition(), scene.RowWidths);
        }
    }

    private void Dropped()
    {
        _tips.Quiet(false);
        _gestures.Let();
        _actions.RequestPaint();
    }

    private bool CornerCursor(OverlayConfig overlay)
    {
        if (!OverlayGestures.InCorner(overlay, _painted, WindowPosition(), _actions.Cursor()))
        {
            return false;
        }

        PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_SIZENWSE));
        return true;
    }

    private void Clicked(Press press, bool locked)
    {
        if (Snaps.Dragged(press, _actions.Cursor(), DragDistance(), locked) is null)
        {
            _actions.Select(new Select(_index + 1, ItemAt(_actions.Cursor()), SelectHow.Click));
        }
    }

    // The pointer entering a value, or leaving it for another, the edge or outside; Windows says when it leaves only once asked.
    private unsafe void Point(string? item)
    {
        if (item == _pointed)
        {
            return;
        }

        if (_pointed is { } left)
        {
            _actions.Select(new Select(_index + 1, left, SelectHow.PointOut));
        }

        if (item is not null)
        {
            _actions.Select(new Select(_index + 1, item, SelectHow.PointIn));
            var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE, hwndTrack = Hwnd };
            PInvoke.TrackMouseEvent(ref track);
        }

        _pointed = item;
    }

    // The item drawn under a point of the screen, or null on the overlay's edge.
    private string? ItemAt(Position cursor)
    {
        var corner = WindowPosition();
        var at = new PointF(cursor.X - corner.X, cursor.Y - corner.Y);
        return _painter.Places.FirstOrDefault(place => place.Item.Contains(at) && place.Id.Length > 0)?.Id;
    }

    // A layered window that is also transparent lets every click through, its pills included.
    private void ClickThrough(bool on)
    {
        const nint Transparent = (nint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        var style = PInvoke.GetWindowLongPtr(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var wanted = on ? style | Transparent : style & ~Transparent;
        if (wanted != style)
        {
            PInvoke.SetWindowLongPtr(Hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, wanted);
        }
    }

    private Position WindowPosition()
    {
        PInvoke.GetWindowRect(Hwnd, out var rect);
        return new Position(rect.left, rect.top);
    }

    private DragDistance DragDistance() => Desktop.DragDistanceAt(PInvoke.GetDpiForWindow(Hwnd));

    private void ShowMenu()
    {
        _tips.Quiet(true);
        _actions.Menu(_index);
        _tips.Quiet(false);
    }

    /// <summary>
    /// The overlay as its last paint drew it, painted afresh without what only the open window or a broken file adds; the
    /// "Sensors stopped" pill while they are. Null before the first paint.
    /// </summary>
    internal Bitmap? Picture()
    {
        if (_painted is not { } painted || _scene is null)
        {
            return null;
        }

        using var painter = new Painter();
        IReadOnlyList<OverlayItem> items = [.. painted.Items.Where(item => item.Id.Length > 0)];
        return painted.Stopped || items.Count == 0
            ? painter.PaintStopped(Translations.Of(OverlayText.SensorsStopped), painted.Look, painted.Scale, items)
            : painter.Paint(items, painted.Look, painted.Scale);
    }

    internal void UndoGesture() => _gestures.Undo();
}
