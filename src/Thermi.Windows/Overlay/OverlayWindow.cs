using System.Drawing;
using System.Globalization;
using Thermi.App.SettingsFile;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Gestures;
using Thermi.Windows.Painting;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using Select = Thermi.Core.Messages.Select;
using SelectHow = Thermi.Core.Messages.SelectHow;

namespace Thermi.Windows.Overlay;

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
    public bool OnScreen { get; private set; }
    public bool Dragging => _mouse.Dragged is not null || _gestures.Drag is not null;

    // The last scene painted, which the glow's frames paint again, and what it drew.
    private OverlayScene? _scene;
    private PaintedOverlay? _painted;

    /// <summary>Whether a value's tooltip may show now, which Windows' tooltip control never says itself.</summary>
    internal bool TipsShow => _tips.Speaks;

    // The item the pointer is on, which the open window marks on its row.
    private string? _pointed;

    public OverlayWindow(int index, Settings settings, OverlayActions actions)
    {
        _index = index;
        _settings = settings;
        _actions = actions;
        _glow = new CriticalGlow(actions.Time);
        _started = actions.Time.GetTimestamp();
        Hwnd = TopLevelWindow.Create(ClassName, ExStyle, WINDOW_STYLE.WS_POPUP);
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

    // Without a class cursor Windows leaves the last cursor shown, often the busy one; without CS_DBLCLKS, a double-click is two presses.
    internal static WNDCLASSEXW WindowClass(WNDPROC procedure, PCWSTR className) => HiddenWindow.Plain(procedure, className) with
    {
        style = WNDCLASS_STYLES.CS_DBLCLKS,
        hCursor = PInvoke.LoadCursor(HINSTANCE.Null, PInvoke.IDC_ARROW),
    };

    /// <summary>
    /// The scale the overlay is drawn at, times the size it shows at: the monitor's it is about to show on among
    /// <paramref name="screens"/>, so a snap to a monitor of another scale paints right at once, or its window's,
    /// <paramref name="dpi"/>, while it knows none.
    /// </summary>
    internal static float ScaleOf(uint dpi, OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        (float)((Snaps.ScreenOf(overlay, screens) is { } screen ? screen.Scale : dpi / 96.0) * Snaps.SizeShown(overlay, screens));

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
        if (OnScreen)
        {
            _topmost.Raise();
        }
    }

    /// <summary>Takes the overlay off the screen until a paint shows it again; it keeps its history meanwhile.</summary>
    public void Hide()
    {
        if (OnScreen)
        {
            PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
            OnScreen = false;
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
                _tips.Left();
                return true;
            case PInvoke.WM_MOUSEWHEEL:
                return _gestures.Wheel(wParam, _scene?.Screens ?? []);
            case PInvoke.WM_SETCURSOR:
                return _gestures.CornerCursor(overlay, _painted, WindowPosition());
            case PInvoke.WM_TIMER when wParam == OverlayFade.TimerId:
                _fade.Now(overlay, _painted?.Scale, _scene?.WindowOpen == true, _gestures.Holds, OnScreen);
                return true;
            case PInvoke.WM_TIMER when wParam == OverlayGestures.HoldTimer:
                _gestures.Hold();
                return true;
            case PInvoke.WM_TIMER when wParam == ValueTips.QuietTimer:
                _tips.Woke();
                return true;
            case PInvoke.WM_HOTKEY when wParam == OverlayGestures.EscapeKey:
                Cancel();
                return true;
            case PInvoke.WM_TIMER when OnScreen && _scene is { } scene && _index < scene.Config.Overlays.Count:
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
        TopLevelWindow.ClickThrough(Hwnd, overlay.ClickThrough);
        var look = marks.Applied(OverlayLook.Of(overlay.Style, scene.LightTaskbar, scene.HighContrast), held: heldAt is not null);
        // A pill held in a drag goes where the pointer takes it, whose monitor's scale the window gets as it crosses.
        IReadOnlyList<Screen> screens = heldAt is null ? scene.Screens : [];
        var overlays = scene.Config.Overlays.Select((each, index) => index == _index ? overlay : each).ToList();
        var items = scene.WithProblem(texts, shown.Items);
        var room = TaskbarRow.RoomOf(overlays, _index, screens, _actions.Shown());
        var scale = _painter.Within(items, look, ScaleOf(PInvoke.GetDpiForWindow(Hwnd), overlay, screens), room);
        scale = _painter.Fitting(items, look, scale, Snaps.Most(overlay, screens));
        _glow.Update(items, scene.Animate);
#pragma warning disable CA2000 // The painter reuses the bitmap while its size holds, and disposes it itself.
        var bitmap = shown.SensorsStopped
            ? _painter.PaintStopped(texts.Of(OverlayText.SensorsStopped), look, scale, items)
            : _painter.Paint(items, look, scale, _glow.Glowing ? _glow.Of : null);
#pragma warning restore CA2000
        marks.Draw(bitmap, shown.SensorsStopped ? [] : _painter.Places, items, scale);
        _gestures.Mark(bitmap, shown.SensorsStopped ? [] : _painter.Places, scale, look.Orientation == Orientation.Vertical, scene.Accent);
        OverlayGestures.HoldCorner(bitmap, overlay, scale);
        Glow();
        _painted = new PaintedOverlay(items, look, scale, bitmap.Size) { Stopped = shown.SensorsStopped };
        var running = _actions.Time.GetElapsedTime(_started);
        Tips(scene, input, shown);
        var at = heldAt ?? TaskbarRow.Locate(overlays, _index, scene.Screens, (bitmap.Width, bitmap.Height), _actions.Shown(), running) ?? WindowPosition();
        _picture.Show(Hwnd, bitmap, at, _fade.Alpha);
        _fade.Watch(overlay, scale, scene.WindowOpen, _gestures.Holds, OnScreen);
        if (!OnScreen)
        {
            const SET_WINDOW_POS_FLAGS Shown = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
            PInvoke.SetWindowPos(Hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, Shown);
            OnScreen = true;
        }

        _tips.After(_actions.Quiet);
    }

    private TrayInput InputOf(OverlayScene scene, Texts texts) =>
        new(scene.Snapshot, scene.Service, _actions.Culture, texts, scene.LightTaskbar, "")
        {
            History = _actions.History,
            WindowOpen = scene.WindowOpen,
            BaseClock = scene.Cpu?.BaseClock,
            Busiest = scene.Cpu?.Busiest,
        };

    // Each value's tooltip where the painter drew its item, the "Sensors stopped" pill's over the whole pill.
    private void Tips(OverlayScene scene, TrayInput input, Core.Tray.OverlayScene shown)
    {
        _tips.Theme(!scene.LightApps && scene.HighContrast is null);
        _tips.Update(_painter.Places, shown.Tooltips.Count, at => ValueTooltips.Text(scene.Config, _index, input, shown.Tooltips[at]));
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
        var step = _mouse.On(message, _actions.Cursor(), WindowPosition(), DragDistance(), overlay.Locked, OverlayGestures.Escape());
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
                var dropped = step.Moved || !Clicked(step.Press!, overlay.Locked);
                _gestures.Release(step.Press!, DragDistance());
                Dropped(dropped);
                return true;
            case MouseAction.Lost:
                _gestures.LetMouseGo();
                Dropped(false);
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
        _tips.Quiet(Hush.Press, true);
        _gestures.Let();
        if (_painted is { } painted && _scene is { } scene)
        {
            var (x, y) = OnPicture(_actions.Cursor());
            _gestures.Press(overlay, painted, scene.Screens, _painter.Places.Pressed(x, y), WindowPosition());
        }
    }

    // A drop leaves the pointer on a value of this overlay or another, whose tooltip waits until the pointer leaves or a
    // while passes: this overlay's from the drop on, as its press lets go, the others' as each hears at its next paint.
    private void Dropped(bool dragged)
    {
        if (dragged)
        {
            _actions.Quiet.Dropped();
            _tips.After(_actions.Quiet);
        }

        _tips.Quiet(Hush.Press, false);
        _gestures.Let();
        _actions.RequestPaint();
    }

    // Esc, the overlay's hotkey while pressed, puts it back as another window taking the mouse does.
    private void Cancel()
    {
        if (_mouse.Cancel())
        {
            _gestures.LetMouseGo();
            Dropped(false);
        }
    }

    private bool Clicked(Press press, bool locked)
    {
        if (Snaps.Dragged(press, _actions.Cursor(), DragDistance(), locked) is not null)
        {
            return false;
        }

        _actions.Select(new Select(_index + 1, ItemAt(_actions.Cursor()), SelectHow.Click));
        return true;
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
        _tips.On(item is not null);
    }

    /// <summary>Where a drop at a point of the screen joins this overlay, as <see cref="PaintedPlaces.JoinAt"/> says.</summary>
    internal DropTarget JoinAt(Position cursor, string? alone) =>
        OnPicture(cursor) is var (x, y) ? _painter.Places.JoinAt(_index, x, y, alone, _painted?.Look.Orientation == Orientation.Vertical) : default;

    internal string? ItemAt(Position cursor) => OnPicture(cursor) is var (x, y) ? _painter.Places.At(x, y)?.Id : null;

    /// <summary>The opacity the overlay shows at, 255 for whole.</summary>
    internal byte Alpha => _fade.Alpha;

    private (double X, double Y) OnPicture(Position cursor)
    {
        var corner = WindowPosition();
        return (cursor.X - corner.X, cursor.Y - corner.Y);
    }

    private Position WindowPosition()
    {
        PInvoke.GetWindowRect(Hwnd, out var rect);
        return new Position(rect.left, rect.top);
    }

    private DragDistance DragDistance() => Desktop.DragDistanceAt(PInvoke.GetDpiForWindow(Hwnd));

    private void ShowMenu()
    {
        _tips.Quiet(Hush.Menu, true);
        _actions.Menu(_index, ItemAt(_actions.Cursor()));
        _tips.Quiet(Hush.Menu, false);
    }

    internal void Separate(string item) => _gestures.Separate(item, _painted, _scene?.Screens ?? []);

    /// <summary>The overlay as last painted, afresh without what only the window or a broken file adds; "Sensors stopped" while they are.</summary>
    internal Bitmap? Picture() => _painted is { } painted && _scene is not null ? painted.Picture(Translations.Of(OverlayText.SensorsStopped)) : null;

    internal void UndoGesture() => _gestures.Undo();
}
