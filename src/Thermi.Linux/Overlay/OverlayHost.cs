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
using Thermi.Linux.Logging;

namespace Thermi.Linux.Overlay;

/// <summary>
/// thermi's overlay process on GNOME: one GLib main loop runs the Shell's link, the paints and the menu, and what comes
/// from other threads, the readings, the settings file and the once-a-second tick, is posted to it. Each overlay of the
/// settings shows through the Shell's extension as Core's <see cref="OverlayVisibility"/> says, and nothing shows until
/// the extension runs thermi's protocol.
/// </summary>
internal sealed class OverlayHost : IDisposable
{
    // Readings drift from a second apart, so the tick paints only once they are late, never a second time between two.
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(1.5);

    private readonly Gio.DBusConnection _bus;
    private readonly ShellLink _link;
    private readonly ISensorFeed _feed;
    private readonly OverlayParts _parts;
    private readonly DesktopNotices _notices;
    private readonly ExtensionArrival _arrival;
    private readonly OverlayNotices _told;
    private readonly OverlayMenu _menu;
    private readonly List<ShellOverlay> _overlays = [];
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

    public OverlayHost(Gio.DBusConnection bus, Settings settings, ISensorFeed feed, TraceEvents trace, ILogger log)
    {
        _bus = bus;
        _feed = feed;
        _trace = trace;
        var time = trace.Time;
        _link = new ShellLink(bus);
        _parts = new OverlayParts(settings, _link, time, CultureInfo.CurrentCulture, log)
        {
            RequestPaint = () => Post(Repaint),
            OpenSettings = (overlay, item) => _menu!.OpenSettings(overlay, item),
            Menu = (overlay, x, y, item) => _menu!.Show(overlay, x, y, item, feed.State, feed.Latest),
            Settle = Settle,
            Dropped = Dropped,
            Traced = trace.On,
        };
        _menu = new OverlayMenu(_parts, _loop.Quit);
        _notices = new DesktopNotices(bus);
        _arrival = new ExtensionArrival(_link, _notices, () => TextsOf(settings.Config), log);
        _told = new OverlayNotices(_parts, _notices, () => TextsOf(settings.Config));
    }

    private Settings Settings => _parts.Settings;

    private ILogger Log => _parts.Log;

    /// <summary>Runs until "Exit thermi": 0, or 0 at once when another overlay of the session runs.</summary>
    public int Run()
    {
        if (!_link.Own())
        {
            Log.AlreadyRunning();
            return 0;
        }

        _link.Ready += protocol => Safe(() => Arrived(protocol));
        _link.MonitorsChanged += monitors => Safe(() => Monitors(monitors));
        _link.Pointer += pointer => Safe(() => Pointer(pointer));
        _link.Key += (overlay, key) => Safe(() => Key(overlay, key));
        _link.Chosen += (_, row) => Safe(() => _menu.Chosen(row));
        _feed.Received += (_, _) => Post(Record);
        Settings.Changed += (_, _) => Post(SettingsChanged);
        using var watcher = Settings.Watch(Log);
        Settings.FileChanged();
        _arrival.Start();
        if (_arrival.Ready)
        {
            Arrived(ShellLink.Protocol);
        }

        using var ticks = _parts.Time.CreateTimer(_ => Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _loop.Run();
        _overlays.ForEach(overlay => _ = overlay.Hide());
        return 0;
    }

    public void Dispose()
    {
        _overlays.ForEach(overlay => overlay.Dispose());
        _link.Dispose();
        _notices.Dispose();
        _loop.Dispose();
    }

    // GLib runs it on the main loop's thread once it is free, and only once.
    private void Post(Action action) => GLib.Functions.IdleAdd(0, () =>
    {
        Safe(action);
        return false;
    });

    // GLib calls these from native code, where an exception that escapes ends the process.
    private void Safe(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // The next paint sends everything again, so the overlay goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.PaintFailed(ex);
        }
    }

    private void Arrived(uint protocol)
    {
        if (!_arrival.Arrived(protocol))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var monitors = await _link.MonitorsAsync();
            Post(() => Monitors(monitors));
        });
    }

    private void Monitors(IReadOnlyList<ShellMonitor> monitors)
    {
        _screens = new ShellScreens(monitors, ShellScreens.Names(_bus));
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

        _parts.History.Add([.. Settings.Config.Overlays.Select(overlay => (overlay, SensorMap.Read(snapshot, overlay)))]);
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
        if (!Settings.Loaded || !_arrival.Ready || _screens.Monitors.Count == 0)
        {
            return;
        }

        var config = Settings.Config;
        while (_overlays.Count < config.Overlays.Count)
        {
            _overlays.Add(new ShellOverlay(_overlays.Count, _parts));
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
        Announce(sent, second);
        _told.Update(config);
    }

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

    private OverlayScene SceneOf(ThermiConfig config)
    {
        var input = new TrayInput(_feed.Latest, _feed.State, _parts.Culture, TextsOf(config), DesktopLooks.LightPanels, "")
        {
            History = _parts.History,
            HideOverlays = _parts.Visibility.HideOverlays,
            HighContrast = _highContrast is not null,
            BaseClock = _baseClock,
        };
        return new OverlayScene(config, Settings.Problem, _screens, input, _highContrast);
    }

    private List<PixelRect?> Boxes() => [.. _overlays.Select(overlay => overlay.Box)];

    private void Pointer(ShellPointer pointer)
    {
        if (pointer.Overlay < _overlays.Count)
        {
            _overlays[(int)pointer.Overlay].Pointer(pointer, _screens);
        }
    }

    private void Key(uint overlay, string key)
    {
        if (key == "Escape" && overlay < _overlays.Count)
        {
            _overlays[(int)overlay].Cancel(_screens);
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
