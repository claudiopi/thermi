using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.LayerShell;
using Thermi.Linux.Logging;
using Thermi.Linux.X11;

namespace Thermi.Linux.Overlay;

/// <summary>
/// thermi's overlay process on Linux: one GLib main loop runs the desktop's surfaces, the paints and the menu, and what
/// comes from other threads, the readings, the settings file and the once-a-second tick, is posted to it. Each overlay of
/// the settings shows through the surfaces as Core's <see cref="OverlayVisibility"/> says, and nothing shows until they
/// are ready, as GNOME's once its extension runs thermi's protocol.
/// </summary>
internal sealed class OverlayHost : IDisposable
{
    // Readings drift from a second apart, so the tick paints only once they are late, never a second time between two.
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(1.5);

    private readonly Gio.DBusConnection _bus;
    private readonly ShellLink _link;
    private readonly SurfaceKind _kind;
    private readonly IOverlaySurfaces _surfaces;
    private readonly ISensorFeed _feed;
    private readonly OverlayParts _parts;
    private readonly DesktopNotices _notices;
    private readonly ThermiDesk _desk;
    private readonly OverlayMenu _menu;
    private readonly List<DesktopOverlay> _overlays = [];
    private readonly HighContrastColors? _highContrast = DesktopLooks.HighContrast();
    private readonly double? _baseClock = DesktopLooks.BaseClock();
    private readonly GLib.MainLoop _loop = GLib.MainLoop.New(null, false);
    private ShellScreens _screens = new([], new Dictionary<(int X, int Y), string>());
    private readonly TraceEvents _trace;
    private long? _readAt;

    // What --trace still owes: each once the paint after what made it is on screen.
    private bool _shownOwed = true;
    private bool _wasHidden;
    private bool _loaded;
    private bool _editOwed;
    private bool _droppedOwed;
    private bool _droppedOnSave;
    private bool _watching;

    public OverlayHost(Gio.DBusConnection bus, Settings settings, ISensorFeed feed, TraceEvents trace, ILogger log, ThermiStart start)
    {
        _bus = bus;
        _feed = feed;
        _trace = trace;
        var kind = start.Kind;
        _kind = kind;
        var time = trace.Time;
        _link = new ShellLink(bus, log);
        _notices = new DesktopNotices(bus, log);
        var arrival = new ExtensionArrival(_link, _notices, () => TextsOf(settings.Config), log);
        _surfaces = kind switch
        {
            SurfaceKind.Layer => new LayerSurfaces(),
            SurfaceKind.X11 => new X11Surfaces(),
            SurfaceKind.Shell or SurfaceKind.None => new ShellSurfaces(_link, arrival),
            _ => new ShellSurfaces(_link, arrival),
        };
        _parts = new OverlayParts(settings, _link, _surfaces, time, CultureInfo.CurrentCulture, log)
        {
            RequestPaint = () => Post(Repaint),
            Post = Post,
            OpenSettings = (overlay, item) => _menu!.OpenSettings(overlay, item),
            Menu = (overlay, x, y, item) => _menu!.Show(overlay, x, y, item, feed.State, feed.Latest),
            Settle = Settle,
            Dropped = Dropped,
            Shown = Boxes,
            JoinAt = (onto, cursor, alone) => onto < _overlays.Count ? _overlays[onto].JoinAt(cursor, alone) : default,
            Trace = Traced,
            Accent = DesktopLooks.Accent(_highContrast),
            Traced = trace.On,
            WindowOpening = () => _desk!.Notices.WindowOpened(),
            WindowFailed = () => _desk!.Notices.WindowFailed(),
        };
        _menu = new OverlayMenu(_parts, _loop.Quit, _overlays, () => _screens);
        _desk = new ThermiDesk(bus, _parts, _menu, _notices, trace, feed, start);
    }

    private Settings Settings => _parts.Settings;

    private ILogger Log => _parts.Log;

    /// <summary>
    /// Runs until "Exit thermi", from the menu or from the settings window's <c>quit</c>, which only the person's own session
    /// bus reaches: 0, or 0 at once when another overlay of the session runs.
    /// </summary>
    public int Run()
    {
        if (!_link.Own())
        {
            Log.AlreadyRunning();
            return 0;
        }

        _surfaces.Arrived += () => Safe(Arrived, Log.ArrivalFailed);
        _surfaces.MonitorsChanged += monitors => Safe(() => Monitors(monitors), Log.MonitorsFailed);
        _surfaces.Pointer += pointer => Safe(() => Pointer(pointer), Log.PointerFailed);
        _surfaces.Key += (overlay, key) => Safe(() => Key(overlay, key), Log.PointerFailed);
        _link.Chosen += (_, row) => Safe(() => _menu.Chosen(row), Log.ChoiceFailed);
        _feed.Received += (_, _) => Post(Record);
        Settings.Changed += (_, _) => Post(SettingsChanged);
        using var watcher = Settings.Watch(Log);
        Settings.FileChanged();
        _surfaces.Start();

        using var ticks = _parts.Time.CreateTimer(_ => Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        using var quit = Gio.SimpleAction.New("quit", null);
        quit.OnActivate += (_, _) => Safe(_loop.Quit, Log.ChoiceFailed);
        using var actions = Gio.SimpleActionGroup.New();
        actions.AddAction(quit);
        var exported = _bus.ExportActionGroup(ShellLink.ObjectPath, actions);
        _loop.Run();
        _bus.UnexportActionGroup(exported);
        _overlays.ForEach(overlay => _ = overlay.Hide());
        return 0;
    }

    public void Dispose()
    {
        _overlays.ForEach(overlay => overlay.Dispose());
        _desk.Dispose();
        _surfaces.Dispose();
        _link.Dispose();
        _notices.Dispose();
        _loop.Dispose();
    }

    // GLib runs it on the main loop's thread once it is free, and only once.
    private void Post(Action action) => GLib.Functions.IdleAdd(0, () =>
    {
        Safe(action, Log.PaintFailed);
        return false;
    });

    private static void Safe(Action action, Action<Exception> failed) => Callbacks.Safely(action, failed);

    private void Arrived()
    {
        _overlays.ForEach(overlay => overlay.Forget());
        _desk.Arrived();
        _watching = false;
        _ = Task.Run(async () =>
        {
            try
            {
                var monitors = await _surfaces.MonitorsAsync();
                Post(() => Monitors(monitors));
            }
            catch (GLib.GException ex)
            {
                // The surfaces' next MonitorsChanged, or their next arrival, brings them.
                Log.MonitorsFailed(ex);
            }
        });
    }

    private void Monitors(IReadOnlyList<ShellMonitor> monitors)
    {
        _screens = new ShellScreens(monitors, _kind == SurfaceKind.Shell ? ShellScreens.Names(_bus) : new Dictionary<(int X, int Y), string>());
        Repaint();
    }

    private void Tick()
    {
        if (_readAt is not { } read || _parts.Time.GetElapsedTime(read) >= Late)
        {
            Repaint(second: true);
        }
    }

    private void Record()
    {
        if (_feed.Latest is not { } snapshot)
        {
            return;
        }

        var config = Settings.Config;
        _parts.History.Add([.. config.Overlays.Select(overlay => (overlay, SensorMap.Read(snapshot, overlay)))]);
        _desk.Notices.Readings(config, snapshot, _screens.Monitors.Any(monitor => monitor.FullScreen));
        _readAt = _parts.Time.GetTimestamp();
        Repaint(second: true);
    }

    // The first is the file's first read; each after, a change saved: a drop's, when one waits on it, or else an edit.
    private void SettingsChanged()
    {
        _editOwed |= _loaded && !_droppedOnSave;
        _loaded = true;
        _droppedOwed |= _droppedOnSave;
        _droppedOnSave = false;
        Repaint();
    }

    private void Dropped(bool written)
    {
        if (written)
        {
            _droppedOnSave = true;
            return;
        }

        _droppedOwed = true;
        Repaint();
    }

    private void Repaint() => Repaint(second: false);

    // A second is one of the service's taken, or one late without any, which --trace counts.
    private void Repaint(bool second)
    {
        if (!Settings.Loaded)
        {
            return;
        }

        var config = Settings.Config;
        _desk.Notices.Update(config);
        if (!_surfaces.Ready || _screens.Monitors.Count == 0)
        {
            // Nothing shows, but the second is thermi's all the same.
            if (second)
            {
                _trace.Write(TraceEvents.Paint);
            }

            return;
        }

        while (_overlays.Count < config.Overlays.Count)
        {
            _overlays.Add(new DesktopOverlay(_overlays.Count, _parts));
        }

        while (_overlays.Count > config.Overlays.Count)
        {
            _ = _overlays[^1].Hide();
            _overlays[^1].Dispose();
            _overlays.RemoveAt(_overlays.Count - 1);
        }

        var scene = SceneOf(config);
        var fullScreen = config.Overlays.Select(overlay => _screens.FullScreen(Snaps.ScreenOf(overlay, scene.Screens.Screens))).ToList();
        var shown = _parts.Visibility.Decide(config, fullScreen);
        var sent = _overlays.Select((overlay, at) => shown[at] == OverlayShown.Shown ? overlay.Show(scene, Boxes()) : overlay.Hide()).ToList();
        var primary = _screens.Monitors.FirstOrDefault(monitor => monitor.Primary) ?? _screens.Monitors[0];
        sent.Add(_desk.Tray.Refresh(config, scene.Input, primary.Scale));
        sent.Add(_desk.Hotkeys.Update(config));
        Announce(sent, second);
        Watch(config, shown);
    }

    // The pointer anywhere is told only while an overlay that fades near it shows, so one without costs nothing.
    private void Watch(ThermiConfig config, IReadOnlyList<OverlayShown> shown)
    {
        var fades = config.Overlays.Where((overlay, at) => shown[at] == OverlayShown.Shown && overlay.Fade && !overlay.ClickThrough).Any();
        if (fades != _watching && _surfaces.PointerAnywhere)
        {
            _surfaces.Watch(fades);
            _watching = fades;
        }
    }

    // An event of --trace's, once the surfaces show what made it.
    private void Traced(string name, Task shown) => _ = shown.ContinueWith(
        _ => _trace.Write(name), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    // Each event --trace owes, once the Shell shows what the paint sent.
    private void Announce(List<Task> sent, bool second)
    {
        var hiding = _parts.Visibility.HideOverlays;
        // At start, the overlays are shown once they show what the service says: its first second, or that it is stopped.
        var shown = !hiding && (_wasHidden || (_shownOwed && (_feed.Latest is not null || _feed.State != ServiceState.Running)));
        List<string> owed =
        [
            .. second ? [TraceEvents.Paint] : Array.Empty<string>(),
            .. _editOwed ? [TraceEvents.EditApplied] : Array.Empty<string>(),
            .. _droppedOwed ? [TraceEvents.Dropped] : Array.Empty<string>(),
            .. hiding && !_wasHidden ? [TraceEvents.Hidden] : Array.Empty<string>(),
            .. shown ? [TraceEvents.Shown] : Array.Empty<string>(),
        ];
        (_editOwed, _droppedOwed, _shownOwed, _wasHidden) = (false, false, _shownOwed && !shown && !hiding, hiding);
        if (owed.Count > 0)
        {
            _ = Task.WhenAll(sent).ContinueWith(
                _ => owed.ForEach(name => _trace.Write(name)), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private OverlayPaint SceneOf(ThermiConfig config)
    {
        var input = new TrayInput(_feed.Latest, _feed.State, _parts.Culture, TextsOf(config), DesktopLooks.LightPanels, "")
        {
            History = _parts.History,
            HideOverlays = _parts.Visibility.HideOverlays,
            HighContrast = _highContrast is not null,
            BaseClock = _baseClock,
        };
        return new OverlayPaint(config, Settings.Problem, _screens, input, _highContrast);
    }

    private List<PixelRect?> Boxes() => [.. _overlays.Select(overlay => overlay.Box)];

    // Each pointer the surfaces tell moves the fade of the overlays that fade near it; one off every overlay, as a leave,
    // is out of their reach.
    private void Pointer(ShellPointer pointer)
    {
        var fades = Settings.Config.Overlays.Any(overlay => overlay.Fade);
        _parts.Cursor = pointer.Kind == "leave" && !_surfaces.PointerAnywhere ? null : (_screens.Thermi(pointer.X, pointer.Y), pointer.Modifiers);
        if (pointer.Overlay < _overlays.Count)
        {
            _overlays[(int)pointer.Overlay].Pointer.On(pointer, _screens);
        }

        if (fades)
        {
            Repaint();
        }
    }

    private void Key(uint overlay, string key)
    {
        if (key == "Escape" && overlay < _overlays.Count)
        {
            _overlays[(int)overlay].Pointer.Cancel();
        }
    }

    // The others measured as they show now, each as its painter fits it beside the rest on its taskbar.
    private EditResult Settle(ThermiConfig before, ThermiConfig after)
    {
        var scene = SceneOf(after);
        var screens = _screens.Screens;
        return TaskbarRow.Settled(before, after, screens, Boxes(), (at, _) =>
        {
            var overlay = after.Overlays[at];
            var (width, height) = at < _overlays.Count ? _overlays[at].Measure(scene, overlay) : (0, 0);
            var place = Snaps.Locate(overlay, screens, width, height) ?? new Position(0, 0);
            return new PixelRect(place.X, place.Y, place.X + width, place.Y + height);
        });
    }

    private static Texts TextsOf(ThermiConfig config) => Texts.For(config.Language, CultureInfo.CurrentUICulture);
}
