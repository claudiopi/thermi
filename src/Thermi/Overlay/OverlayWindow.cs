using System.Drawing;
using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Gestures;
using Thermi.Painting;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using Select = Thermi.Core.Messages.Select;
using SelectHow = Thermi.Core.Messages.SelectHow;

namespace Thermi.Overlay;

internal sealed record OverlayActions(CultureInfo Culture, TimeProvider Time, Action RequestPaint, Action<int, string?> OpenSettings, Action Exit, Func<Position> Cursor)
{
    /// <summary>A value or an edge clicked or pointed at, which the open window selects or marks.</summary>
    public Action<Select> Select { get; init; } = _ => { };

    /// <summary>The place of another overlay shown under a point of the screen, the first given apart, or null.</summary>
    public Func<int, Position, int?> OverlayAt { get; init; } = (_, _) => null;

    /// <summary>The last desktop gesture, which the overlays' menus offer to undo.</summary>
    public GestureUndo? Gestures { get; init; }

    /// <summary>Shows an overlay's menu, which the host builds with thermi's.</summary>
    public Action<int> Menu { get; init; } = _ => { };

    /// <summary>Every overlay's items' last minute and running statistics, by item, which the tray icons show too.</summary>
    public ItemHistory History { get; init; } = new(Time);
}

internal sealed record OverlayScene(ThermiConfig Config, ConfigProblem? Problem, Snapshot? Snapshot, IReadOnlyList<Screen> Screens, bool LightTaskbar, HighContrastColors? HighContrast, bool WindowOpen)
{
    public ServiceState Service { get; init; } = ServiceState.Running;

    /// <summary>Whether the window has opened since thermi was first started, after which the tooltips stop offering to set it up.</summary>
    public bool OpenedOnce { get; init; } = true;

    /// <summary>Whether apps are in light mode, which the values' tooltips follow as thermi's menus do.</summary>
    public bool LightApps { get; init; } = true;

    /// <summary>Whether Windows' animation effects are on, without which no pill glows.</summary>
    public bool Animate { get; init; }

    /// <summary>The CPU's base clock, read once, and what is busiest now, read only while a CPU or GPU value is critical.</summary>
    public CpuWatch? Cpu { get; init; }
}

/// <summary>
/// One overlay of the settings, found by its place in them: a layered window that only draws, with its own painter, its
/// own items' history, its menu and the mouse. The host runs its messages.
/// </summary>
internal sealed partial class OverlayWindow : IDisposable
{
    internal const string ClassName = "thermi-overlay";
    internal const WINDOW_EX_STYLE ExStyle = WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOPMOST | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

    // A glow's frames come about 30 times a second, from a timer of the overlay's own, only while it glows.
    private const nuint GlowTimer = 1;
    private const uint GlowFrame = 33;

    private readonly int _index;
    private readonly Settings _settings;
    private readonly OverlayActions _actions;
    private readonly Painter _painter = new(reuse: true);
    private readonly OverlayMouse _mouse = new();
    private readonly Topmost _topmost;
    private readonly ValueTips _tips;
    private readonly CriticalGlow _glow;

    // OLED care moves the overlay by how long it has run.
    private readonly long _started;
    private bool _shown;

    // The last scene painted, which the glow's frames paint again.
    private OverlayScene? _scene;

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

    /// <summary>The scale the overlay is drawn at: its monitor's, times its own size.</summary>
    internal static float ScaleOf(uint dpi, OverlayConfig overlay) => dpi / 96f * (float)overlay.Size;

    /// <summary>
    /// Puts the bitmap on screen at <paramref name="position"/>: its alpha becomes the window's shape, rounded corners and
    /// all, and where clicks pass. Moving and resizing in the same call keeps a snapped edge still as the overlay grows.
    /// </summary>
    internal static void Show(HWND hwnd, Bitmap bitmap, Position position, byte alpha = 255)
    {
        HDC screen = default, memory = default;
        HGDIOBJ hBitmap = default, previous = default;
        try
        {
            screen = PInvoke.GetDC(HWND.Null);
            memory = PInvoke.CreateCompatibleDC(screen);
            hBitmap = (HGDIOBJ)bitmap.GetHbitmap(Color.FromArgb(0));
            previous = PInvoke.SelectObject(memory, hBitmap);
            var blend = new BLENDFUNCTION { SourceConstantAlpha = alpha, AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA };
            PInvoke.UpdateLayeredWindow(hwnd, screen, new Point(position.X, position.Y), new SIZE(bitmap.Width, bitmap.Height), memory, new Point(0, 0), default, blend, UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA);
        }
        finally
        {
            Release(screen, memory, hBitmap, previous);
        }
    }

    public void Dispose()
    {
        _topmost.Dispose();
        _tips.Dispose();
        _drag?.Dispose();
        PInvoke.KillTimer(Hwnd, GlowTimer);
        PInvoke.KillTimer(Hwnd, FadeTimer);
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
            case PInvoke.WM_TIMER when wParam == FadeTimer:
                FadeNow(overlay);
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
        var overlay = _drag?.Shown(scene.Config.Overlays[_index]) ?? scene.Config.Overlays[_index];
        var texts = Texts.For(scene.Config.Language, CultureInfo.CurrentUICulture);
        var input = new TrayInput(scene.Snapshot, scene.Service, _actions.Culture, texts, scene.LightTaskbar, "") { History = _actions.History, WindowOpen = scene.WindowOpen, BaseClock = scene.Cpu?.BaseClock, Busiest = scene.Cpu?.Busiest };
        var shown = ThermiMark.Overlay(scene.Config, _index, input, scene.OpenedOnce);
        var marks = Marks;
        ClickThrough(overlay.ClickThrough);
        var look = marks.Applied(OverlayLook.Of(overlay.Style, scene.LightTaskbar, scene.HighContrast));
        var scale = ScaleOf(PInvoke.GetDpiForWindow(Hwnd), overlay);
        var items = Extras(scene, texts, shown.Items);
        _glow.Update(items, scene.Animate);
#pragma warning disable CA2000 // The painter reuses the bitmap while its size holds, and disposes it itself.
        var bitmap = shown.SensorsStopped
            ? _painter.PaintStopped(texts.Of(OverlayText.SensorsStopped), look, scale)
            : _painter.Paint(items, look, scale, _glow.Glowing ? _glow.Of : null);
#pragma warning restore CA2000
        marks.Draw(bitmap, shown.SensorsStopped ? [] : _painter.Places, items, scale);
        Tips(scene, input, shown);
        Glow();
        _painted = (items, look, scale, bitmap.Size);
        var running = _actions.Time.GetElapsedTime(_started);
        var crowded = !shown.SensorsStopped && TaskbarRoom.Crowded(_painter, items, look, scale, overlay, scene.Screens);
        Show(Hwnd, bitmap, HeldAt ?? Snaps.Locate(overlay, scene.Screens, bitmap.Width, bitmap.Height, running, crowded) ?? WindowPosition(), _alpha);
        WatchPointer(overlay);
        if (!_shown)
        {
            PInvoke.SetWindowPos(Hwnd, HWND.HWND_TOPMOST, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
            _shown = true;
        }
    }

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
                Dragging(step);
                return true;
            case MouseAction.Release:
                PInvoke.ReleaseCapture();
                Clicked(step.Press!, overlay.Locked);
                Released(step);
                return true;
            case MouseAction.Lost:
                Dropped();
                return true;
            case MouseAction.Open when !DoubleClickedCorner(overlay):
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

    private static void Release(HDC screen, HDC memory, HGDIOBJ hBitmap, HGDIOBJ previous)
    {
        if (!previous.IsNull)
        {
            PInvoke.SelectObject(memory, previous);
        }

        if (!hBitmap.IsNull)
        {
            PInvoke.DeleteObject(hBitmap);
        }

        if (!memory.IsNull)
        {
            PInvoke.DeleteDC(memory);
        }

        if (!screen.IsNull)
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }

    private void ShowMenu()
    {
        _tips.Quiet(true);
        _actions.Menu(_index);
        _tips.Quiet(false);
    }
}
