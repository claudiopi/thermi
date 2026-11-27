using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Windows.Gestures;
using Thermi.Windows.Logging;
using Thermi.Windows.Tray;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Overlay;

/// <summary>
/// The overlay process's one hidden top-level window, of class <c>thermi-host</c>, which other processes find by that
/// class: it receives "show" and "hotkey status" from them, <c>TaskbarCreated</c> and the hotkeys, and owns the tray
/// icons whatever shows, which a message-only window could not. It runs the one message loop, the once-a-second tick and
/// the paint throttle every overlay shares, keeps one <see cref="OverlayWindow"/> per overlay of the settings, and shows
/// each as Core's <see cref="OverlayVisibility"/> says.
/// </summary>
internal sealed class OverlayHost : IDisposable
{
    internal const nuint TickTimer = 1;

    private const string ClassName = HiddenWindow.HostClass;
    private const uint PaintNow = PInvoke.WM_APP + 1;
    private const uint SnapshotArrived = PInvoke.WM_APP + 2;
    private const uint SettingsArrived = PInvoke.WM_APP + 3;

    private static readonly uint ShowMessage = PInvoke.RegisterWindowMessage("thermi-show");
    private static readonly uint HotkeyStatusMessage = PInvoke.RegisterWindowMessage("thermi-hotkey-status");

    // Readings drift from a second apart, so the tick paints only once they are late, never a second time between two.
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(1.5);

    private readonly Settings _settings;
    private readonly Func<Snapshot?> _latest;
    private readonly ILogger _log;
    private readonly FileSystemWatcher? _watcher;
    private readonly List<OverlayWindow> _overlays = [];
    private readonly OverlayActions _actions;
    private readonly HostEnvironment _environment;
    private readonly OverlayVisibility _visibility = new();
    private readonly WindowLink _link;
    private readonly DesktopReads _desktop;
    private readonly CpuWatch _cpu = new();

    // Every paint, the tick's included, goes through it, and each comes out on the window's thread.
    private readonly Throttle<bool> _paints;
    private long? _readAt;

    // Held in a field so the garbage collector never frees it while Windows still calls it.
    private readonly WNDPROC _procedure;
    private HotkeyRegistry? _hotkeys;

    // Whether a full-screen app was in front on any monitor at the last paint.
    private bool _fullScreen;

    public OverlayHost(Settings settings, Func<Snapshot?> latest, CultureInfo culture, TimeProvider time, ILogger log, HostEnvironment? environment = null)
    {
        _settings = settings;
        _latest = latest;
        _log = log;
        _environment = environment ?? HostEnvironment.Default;
        _procedure = WndProc;
        _paints = new Throttle<bool>(Settings.Interval, _ => PInvoke.PostMessage(Hwnd, PaintNow, 0, 0), time);
        _desktop = new DesktopReads();
        var history = new Core.Look.ItemHistory(time);
        _link = new WindowLink(new LinkParts(settings, history, _environment, time, log, RequestPaint));
        _actions = new OverlayActions(culture, time, RequestPaint, _link.OpenWindow, () => PInvoke.DestroyWindow(Hwnd), _environment.Cursor)
        {
            History = history,
            OpenSize = _link.OpenSize,
            Select = _link.Selected,
            Shown = () => DesktopDrag.Boxes(_overlays),
            Settle = (before, after) => Ui.Views.OverlayPlaces.Settled(before, after, Scene(after, _desktop.Screens()), culture, DesktopDrag.Boxes(_overlays)),
            JoinAt = (overlay, cursor, alone) => overlay < _overlays.Count ? _overlays[overlay].JoinAt(cursor, alone) : new DropTarget(overlay, null),
            Gestures = new App.Gestures.GestureUndo(time, gesture => _ = _link.WindowOpen && HiddenWindow.Send(HiddenWindow.WindowClass, gesture)),
            Menu = (overlay, item) => Tray?.Menu(overlay, item: item),
        };
        _settings.Changed += SettingsChanged;
        _watcher = settings.Watch(log);
    }

    internal HWND Hwnd { get; private set; }

    /// <summary>Whether the sensor service runs or its data went stale, which the tray icons say while no snapshot is fresh.</summary>
    internal Func<ServiceState> Service { get; init; } = () => ServiceState.Running;

    internal IReadOnlyList<OverlayWindow> Overlays => _overlays;

    internal Core.Look.ItemHistory History => _actions.History;

    /// <summary>The tray icons, their notices and both menus, once started.</summary>
    internal ThermiTray? Tray { get; private set; }

    /// <summary>
    /// Whether this is thermi's first start, which lasts until the window first opens: its notice shows, and the values'
    /// tooltips offer to set thermi up.
    /// </summary>
    internal bool FirstStart { get; init; }

    /// <summary>Windows woke from sleep or hibernation: the sensors are read again at once.</summary>
    internal Action Resumed { get; init; } = () => { };

    /// <summary>Each message from the window, decoded, on the window's thread, after the host has acted on it.</summary>
    internal Action<WindowMessage>? Received { get => _link.Received; init => _link.Received = value; }

    /// <summary>Asks the running overlay process to show, as a second thermi does before it exits; false when none answers.</summary>
    public static bool AskToShow() => HiddenWindow.Ask(ClassName, ShowMessage) is not null;

    /// <summary>Whether an overlay process runs, without asking it anything.</summary>
    public static bool Running() => !PInvoke.FindWindowEx(HWND.Null, HWND.Null, ClassName, null).IsNull;

    /// <summary>
    /// Asks the running overlay which hotkeys it could not register, as the window does: <see cref="Hotkeys.Trusted"/>'s answer, or null for none.
    /// </summary>
    public static long? AskHotkeyStatus() => HiddenWindow.Ask(ClassName, HotkeyStatusMessage);

    /// <summary>A fresh snapshot came, on any thread: each overlay adds it to its history on the window's thread.</summary>
    public void Arrived() => Post(SnapshotArrived);

    public void Start()
    {
        HiddenWindow.Register(ClassName, name => HiddenWindow.Plain(_procedure, name));
        HiddenWindow.Register(OverlayWindow.ClassName, name => OverlayWindow.WindowClass(_procedure, name));
        Hwnd = HiddenWindow.Create(ClassName);
        _hotkeys = new HotkeyRegistry(Hwnd);
        _hotkeys.Register(_settings.Config.Hotkeys);
        Tray = new ThermiTray(new TrayParts(Hwnd, _settings, _visibility, _overlays, _actions, _environment, Service), _log);
        _link.Start(FirstStart);

        // The overlays show once the file is read, so they never show the defaults in place of the user's settings; those
        // read before the host started show at once, since reading the same file again changes nothing.
        _settings.FileChanged();
        RequestPaint();
        PInvoke.SetTimer(Hwnd, TickTimer, 1000, null);
    }

    public void Run()
    {
        Start();
        while (PInvoke.GetMessage(out var msg, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(msg);
            PInvoke.DispatchMessage(msg);
        }
    }

    public void Dispose()
    {
        _settings.Changed -= SettingsChanged;
        _watcher?.Dispose();
        _paints.Dispose();
        _cpu.Dispose();
        _overlays.ForEach(overlay => overlay.Dispose());
        _overlays.Clear();
        Tray?.Dispose();
        _hotkeys?.Dispose();
        if (!Hwnd.IsNull)
        {
            PInvoke.DestroyWindow(Hwnd);
            Hwnd = HWND.Null;
        }

        HiddenWindow.Unregister(OverlayWindow.ClassName);
        HiddenWindow.Unregister(ClassName);
    }

    private LRESULT WndProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (hwnd == Hwnd && message == HotkeyStatusMessage)
            {
                return new LRESULT((nint)(_hotkeys?.Status ?? Hotkeys.Status(null, HotkeyFailures.None)));
            }

            if (hwnd == Hwnd && message == HotkeyRegistry.RestMessage)
            {
                _link.LookForWindow();
                _hotkeys?.Rest(wParam != 0 && _link.WindowOpen);
                return new LRESULT(1);
            }

            if (hwnd == Hwnd && _link.FromWindow(Hwnd, message, wParam, lParam) is { } answer)
            {
                return answer;
            }

            // Windows calls an overlay thermi's main window: a tool closing it exits thermi, not that overlay alone.
            if (message == PInvoke.WM_CLOSE)
            {
                _actions.Exit();
                return new LRESULT(0);
            }

            if (hwnd == Hwnd ? Handle(message, wParam, lParam) : _overlays.Find(overlay => overlay.Hwnd == hwnd)?.Handle(message, wParam) == true)
            {
                return new LRESULT(0);
            }
        }
#pragma warning disable CA1031 // Windows calls this from native code: an exception escaping it ends the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.MessageFailed(ex);
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    // The registered messages and the tray's have no constant values for a switch.
    private bool Handle(uint message, nuint wParam, nint lParam)
    {
        if (message == Topmost.TaskbarCreated)
        {
            Tray?.Restore();
            RequestPaint();
            Raise();
        }
        else if (message == ShowMessage)
        {
            Tick();
        }
        else if (TrayIcons.IsCallback(message))
        {
            Tray?.Event(message, (uint)(lParam & 0xFFFF));
        }
        else
        {
            return HandleOwn(message, wParam);
        }

        return true;
    }

    private bool HandleOwn(uint message, nuint wParam)
    {
        switch (message)
        {
            case PInvoke.WM_TIMER when wParam == TickTimer:
                Tick();
                return true;
            case PaintNow:
                Repaint();
                return true;
            case SnapshotArrived:
                Record();
                return true;
            case WindowLink.HistoryWanted:
                _link.SendHistory();
                return true;
            case SettingsArrived:
                _hotkeys?.Register(_settings.Config.Hotkeys);
                return true;
            case PInvoke.WM_HOTKEY:
                Pressed((int)wParam);
                return true;

            // The taskbar's light or dark mode and the work area come as a setting change, the accent as DWM's, and the
            // overlays and icons follow them at once.
            case PInvoke.WM_DISPLAYCHANGE or PInvoke.WM_SETTINGCHANGE or PInvoke.WM_DWMCOLORIZATIONCOLORCHANGED:
                _desktop.Forget();
                RequestPaint();
                return true;
            case PInvoke.WM_POWERBROADCAST when wParam == PInvoke.PBT_APMRESUMEAUTOMATIC:
                Resumed();
                RequestPaint();
                return true;
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    private void Tick()
    {
        _link.LookForWindow();

        // A window that closed or crashed while a row recorded leaves no hotkey resting.
        if (!_link.WindowOpen)
        {
            _hotkeys?.Rest(false);
        }

        Raise();
        if (_readAt is not { } read || _actions.Time.GetElapsedTime(read) >= Late)
        {
            RequestPaint();
        }
    }

    private void Pressed(int id)
    {
        if (HotkeyRegistry.RoleOf(id) is { } role)
        {
            App.Gestures.HotkeyPresses.Pressed(role, _settings, _visibility, _actions.History);
        }

        RequestPaint();
    }

    private void Raise() => _overlays.ForEach(overlay => overlay.Raise());

    private void RequestPaint() => _paints.Post(true);

    // Hotkeys are registered on the window's thread, which gets their presses.
    private void SettingsChanged(object? sender, EventArgs e)
    {
        Post(SettingsArrived);
        RequestPaint();
    }

    private void Post(uint message) => _ = !Hwnd.IsNull && PInvoke.PostMessage(Hwnd, message, 0, 0);

    private void Record()
    {
        if (_latest() is not { } snapshot)
        {
            return;
        }

        var config = _settings.Config;
        _actions.History.Add([.. config.Overlays.Select(overlay => (overlay, SensorMap.Read(snapshot, overlay)))]);
        _cpu.Sample(ValueTooltips.WantsBusiest(config, snapshot));

        // "Notify me" waits out a full-screen app in front on any monitor, as a game or a presentation, as the last paint
        // found it: one a second at least, so asking Windows again for each reading would only repeat it.
        Tray?.Critical(config, snapshot, _fullScreen);
        _readAt = _actions.Time.GetTimestamp();
        RequestPaint();
    }

    private void Repaint()
    {
        if (!_settings.Loaded)
        {
            return;
        }

        var config = _settings.Config;
        OnePerOverlay(config);

        var screens = _desktop.Screens();
        var scene = Scene(config, screens);
        var editing = _link.Editing(config.Overlays.Count);
        var fullScreen = OverlayVisibility.FullScreen(config, screens, Desktop.Foreground(), Desktop.UserState());
        _fullScreen = fullScreen.Any(full => full);
        var shown = _visibility.Decide(config, fullScreen);
        for (var at = 0; at < _overlays.Count; at++)
        {
            if (shown[at] == OverlayShown.Shown)
            {
                _overlays[at].Marks = _link.Marks(at, editing, config.Overlays[at], scene.LightTaskbar, scene.Accent);
                _overlays[at].Repaint(scene);
            }
            else
            {
                _overlays[at].Hide();
            }
        }

        Tray?.Refresh(new TrayState(scene, _link.WindowOpen, !_link.OpenedOnce));
        _log.Painted();
    }

    private void OnePerOverlay(Core.Settings.ThermiConfig config)
    {
        while (_overlays.Count < config.Overlays.Count)
        {
            _overlays.Add(new OverlayWindow(_overlays.Count, _settings, _actions));
        }

        while (_overlays.Count > config.Overlays.Count)
        {
            _overlays[^1].Dispose();
            _overlays.RemoveAt(_overlays.Count - 1);
        }
    }

    private OverlayScene Scene(Core.Settings.ThermiConfig config, IReadOnlyList<Screen> screens) =>
        new(config, _settings.Problem, _latest(), screens, _desktop.LightTaskbar(), Desktop.HighContrast(), _link.WindowOpen)
        {
            WindowsAccent = _desktop.Accent(),
            Service = Service(),
            Animate = Desktop.Animations(),
            LightApps = _desktop.LightApps(),
            OpenedOnce = _link.OpenedOnce,
            Cpu = _cpu,
        };
}
