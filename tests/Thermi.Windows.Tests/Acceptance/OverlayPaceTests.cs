using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>How often the overlay paints, which is most of what it costs.</summary>
[Collection(nameof(HostRun))]
public sealed class OverlayPaceTests : IDisposable
{
    private readonly SettingsFolder _folder = new();
    private readonly FakeTray _tray = new();
    private readonly string _windowMutex = HostRun.WindowMutex();

    public void Dispose() => _folder.Dispose();

    // The recorded CPU load, its readings about a second apart and drifting from it, as the service's come against the overlay's
    // once-a-second tick: each reading paints once and no tick adds a paint of its own; with the readings stopped, each
    // tick paints, past a moment's grace, so what shows can say they stopped.
    [Fact]
    public async Task TheOverlayPaintsOncePerReadingAndOncePerTickWithoutThem()
    {
        var series = TestRecordings.Series(TestRecordings.CpuLoad);
        double[] apart = [1.0, 1.1, 1.05, 0.9, 1.15, 1.05, 1.0];
        var settings = await _folder.LoadedAsync();
        var log = new ListLogger(LogLevel.Debug);
        var (read, paintsReading, paintsStopped) = (0, 0, 0);

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => series[Math.Min(read, series.Count - 1)], HostRun.English, _folder.Clock, log,
                _tray.Environment(_windowMutex));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            int Paints()
            {
                return log.Entries.Count(entry => entry.Message == "paint");
            }

            void Run(int tenths, Func<int, bool> reads)
            {
                for (var tenth = 1; tenth <= tenths; tenth++)
                {
                    _folder.Clock.Advance(TimeSpan.FromMilliseconds(100));
                    if (reads(tenth))
                    {
                        read++;
                        host.Arrived();
                    }

                    if (tenth % 10 == 5)
                    {
                        HostRun.Tick(host);
                    }

                    HostRun.Pump(host);
                }
            }

            var due = 0.0;
            var next = 0;
            Run(20, _ => false);
            var before = Paints();
            var start = read;
            Run(300, tenth =>
            {
                if (tenth < Math.Round(due * 10))
                {
                    return false;
                }

                due += apart[next++ % apart.Length];
                return true;
            });
            HostRun.Pump(host);
            paintsReading = Paints() - before - (read - start);
            Run(20, _ => false);
            before = Paints();
            Run(80, _ => false);
            paintsStopped = Paints() - before;
        });

        Assert.Equal((0, 8), (paintsReading, paintsStopped));
    }

    // Nobody sees the overlay while the session is locked or its display is off: the readings that come meanwhile go into
    // its history and its notices, which a full-screen app gone from the front no longer holds, and draw nothing; as soon
    // as somebody can see it again it shows the last one, before any tick. A dimmed display still shows it.
    [Theory] // Rows: each a word of its own that Windows sends, read apart
    [InlineData("locked", false)]
    [InlineData("display off", false)]
    [InlineData("display dimmed", true)]
    public async Task AnOverlayNobodySeesPaintsNothingAndShowsTheLastReadingOnceSeen(string unseen, bool seen)
    {
        var hot = OverlayConfig.DefaultItems[0] with { Threshold = new Threshold(critical: 1), Notify = true };
        var overlay = new OverlayConfig { Items = [hot, .. OverlayConfig.DefaultItems.Skip(1)] };
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en", Overlays = [overlay] });
        var sight = new Sight();
        var log = new ListLogger(LogLevel.Debug);
        var now = HostRun.Recorded;
        var (draws, notices, shown, history) = (-1, -1, new List<string>(), new List<IReadOnlyList<double?>>());

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => now, HostRun.English, _folder.Clock, log, sight.Of(_tray.Environment(_windowMutex)));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            shown.Add(Picture(host));
            history.Add(host.History.TenMinutes(hot.Id));
            sight.Front = Sight.FullScreen();
            Seconds(host, 1, () => { });
            sight.Tell(host, unseen, seen: false);
            var before = Draws(log);
            now = HostRun.Warmer;
            Seconds(host, 12, host.Arrived);
            (draws, notices) = (Draws(log) - before, _tray.Calls.Count(call => call.Notice.Length > 0));
            shown.Add(Picture(host));
            history.Add(host.History.TenMinutes(hot.Id));
            sight.Tell(host, unseen, seen: true);
            _folder.Clock.Advance(Settings.Interval);
            HostRun.Pump(host);
            shown.Add(Picture(host));
        });

        Assert.Equal((seen, seen, 1), (draws > 0, shown[0] != shown[1], notices));
        Assert.NotEqual(shown[0], shown[2]);
        Assert.NotEqual(history[0], history[1]);
    }

    // Windows tells of no screen saver, and a word of the display coming back may be missed: the tick finds both within a
    // second. Nothing draws while nobody sees, and one draw shows the overlay again, even with nothing new, since the
    // compositor may have dropped its picture meanwhile.
    [Theory] // Rows: each a way the overlay learns it is seen again, read apart
    [InlineData("screen saver", true)]
    [InlineData("input after the display went off", true)]
    [InlineData("unlocked", false)]
    public async Task TheTickFindsWhetherAnybodySeesTheOverlayAndOneDrawShowsItAgain(string how, bool warmer)
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en", Overlays = [new OverlayConfig { Graph = GraphStyle.None }] });
        var sight = new Sight();
        var log = new ListLogger(LogLevel.Debug);
        var now = HostRun.Recorded;
        var (unseen, again, shown) = (-1, -1, new List<string>());

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => now, HostRun.English, _folder.Clock, log, sight.Of(_tray.Environment(_windowMutex)));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            Seconds(host, 1, host.Arrived);
            shown.Add(Picture(host));
            sight.Leave(host, how);
            Seconds(host, 1, host.Arrived);
            var before = Draws(log);
            now = warmer ? HostRun.Warmer : now;
            Seconds(host, 3, host.Arrived);
            unseen = Draws(log) - before;
            sight.Return(host, how);
            Seconds(host, 1, host.Arrived);
            again = Draws(log) - before - unseen;
            shown.Add(Picture(host));
        });

        Assert.Equal((0, 1, warmer), (unseen, again, shown[0] != shown[1]));
    }

    // A second that shows what the last one showed draws nothing; a new value draws, and so does a window something else
    // moved, which the draw puts back where it belongs.
    [Fact]
    public async Task AnOverlayDrawsOnlyWhatChangedAndPutsBackAWindowMovedFromOutside()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en", Overlays = [new OverlayConfig { Graph = GraphStyle.None }] });
        var log = new ListLogger(LogLevel.Debug);
        var now = HostRun.Recorded;
        var (same, warmer, moved, back) = (-1, -1, -1, false);

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => now, HostRun.English, _folder.Clock, log, _tray.Environment(_windowMutex));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            Seconds(host, 2, host.Arrived);
            var before = Draws(log);
            Seconds(host, 3, host.Arrived);
            same = Draws(log) - before;
            now = HostRun.Warmer;
            Seconds(host, 1, host.Arrived);
            warmer = Draws(log) - before - same;
            var window = host.Overlays[0].Hwnd;
            PInvoke.GetWindowRect(window, out var placed);
            const SET_WINDOW_POS_FLAGS Moved = SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
            PInvoke.SetWindowPos(window, HWND.Null, placed.left + 50, placed.top - 50, 0, 0, Moved);
            Seconds(host, 1, host.Arrived);
            moved = Draws(log) - before - same - warmer;
            back = PInvoke.GetWindowRect(window, out var after) && (after.left, after.top) == (placed.left, placed.top);
        });

        Assert.Equal((0, 1, 1, true), (same, warmer, moved, back));
    }

    // High contrast and the animation effects are kept as Windows first answered, whatever paints between, until it says a
    // setting or a system color changed: then the overlay follows them, in its colors, or in the glow of a first critical.
    [Theory] // Rows: each a message of its own the host reads apart, or a read of its own it forgets
    [InlineData("high contrast", 0u, false)]
    [InlineData("high contrast", PInvoke.WM_SETTINGCHANGE, true)]
    [InlineData("high contrast", PInvoke.WM_SYSCOLORCHANGE, true)]
    [InlineData("animations", PInvoke.WM_SETTINGCHANGE, true)]
    public async Task WhatWindowsSaysOfItsLookIsKeptUntilItSaysItChanged(string read, uint message, bool follows)
    {
        var flat = new OverlayConfig { Graph = GraphStyle.None };
        var hot = OverlayConfig.DefaultItems[0];
        var celsius = SensorMap.Read(HostRun.Recorded, flat)[Metrics.CpuTemperature.Id].Value!.Value;
        var overlay = flat with { Items = [hot with { Threshold = new Threshold(critical: celsius + 1.5) }] };
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en", Overlays = [overlay] });
        var (contrast, animations, now) = ((HighContrastColors?)null, false, HostRun.Recorded);
        var environment = _tray.Environment(_windowMutex) with { Reads = () => new DesktopReads(() => contrast, () => animations) };
        var log = new ListLogger(LogLevel.Debug);
        var (shown, glowing) = (new List<string>(), false);

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => now, HostRun.English, _folder.Clock, log, environment);
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            Seconds(host, 1, host.Arrived);
            shown.Add(Picture(host));
            (contrast, animations) = read == "high contrast" ? (Contrast, false) : (null, true);
            Seconds(host, 3, host.Arrived);
            shown.Add(Picture(host));
            if (message != 0)
            {
                // Sent, not posted: Windows refuses to post a setting change, whose lParam may point to a string.
                PInvoke.SendMessage(host.Hwnd, message, 0, 0);
            }

            Seconds(host, 1, host.Arrived);
            shown.Add(Picture(host));
            var before = Draws(log);
            now = HostRun.Warmer;
            Seconds(host, 2, host.Arrived);
            glowing = Draws(log) - before > 1;
        });

        Assert.Equal((true, follows), (shown[0] == shown[1], read == "high contrast" ? shown[1] != shown[2] : glowing));
    }

    // Colors no overlay's own look has, so a picture in them is another.
    private static HighContrastColors Contrast { get; } = new(Rgb.Parse("#FFFF00"), Rgb.Parse("#0000FF"), Rgb.Parse("#FF00FF"), Rgb.Parse("#00FF00"));

    private static int Draws(ListLogger log) => log.Entries.Count(entry => entry.Message == "draw");

    // Seconds on the clock, each with what comes in it and the tick, and the paints they ask for let out.
    private void Seconds(OverlayHost host, int seconds, Action each)
    {
        for (var second = 0; second < seconds; second++)
        {
            _folder.Clock.Advance(TimeSpan.FromSeconds(1));
            each();
            HostRun.Tick(host);
        }

        _folder.Clock.Advance(Settings.Interval);
        HostRun.Pump(host);
    }

    private static string Picture(OverlayHost host)
    {
        using var picture = host.Overlays[0].Picture();
        using var png = new MemoryStream();
        picture?.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(png.ToArray());
    }

    /// <summary>The session as a test has it: away or not, its last input, and the window in front, which a lock takes away.</summary>
    private sealed class Sight
    {
        private bool _away;
        private TimeSpan _sinceInput = TimeSpan.MaxValue;

        public ForegroundWindow? Front { get; set; }

        public HostEnvironment Of(HostEnvironment environment) =>
            environment with { Foreground = () => Front, Sight = FakeTray.Watched with { State = State, SinceInput = () => _sinceInput } };

        private NotificationState State() => _away ? NotificationState.Away : NotificationState.Normal;

        // Nobody sees the session: its screen saver starts, of which Windows says nothing, its display goes off, or it locks.
        public void Leave(OverlayHost host, string how)
        {
            if (how == "screen saver")
            {
                _away = true;
                return;
            }

            Tell(host, how == "unlocked" ? "locked" : "display off", seen: false);
        }

        // Somebody sees it again: the screen saver ends, a key or the mouse wakes the display with no word, or it unlocks.
        public void Return(OverlayHost host, string how)
        {
            if (how == "screen saver")
            {
                _away = false;
            }
            else if (how == "unlocked")
            {
                Tell(host, "locked", seen: true);
            }
            else
            {
                _sinceInput = TimeSpan.Zero;
            }
        }

        // A window over the whole of the monitor the overlay snaps to, as a game's.
        public static ForegroundWindow FullScreen()
        {
            var screen = Desktop.Screens().First(each => each.Primary).Bounds;
            return new ForegroundWindow("Game", screen, Maximized: false, Minimized: false) { Monitor = screen };
        }

        // As Windows says it: a lock or unlock of the session, or the session's display off, dimmed or on; the app in front
        // goes meanwhile.
        public unsafe void Tell(OverlayHost host, string unseen, bool seen)
        {
            Front = null;
            if (unseen == "locked")
            {
                _away = !seen;
                PInvoke.SendMessage(host.Hwnd, PInvoke.WM_WTSSESSION_CHANGE, seen ? PInvoke.WTS_SESSION_UNLOCK : PInvoke.WTS_SESSION_LOCK, 0);
                return;
            }

            var buffer = stackalloc byte[sizeof(POWERBROADCAST_SETTING) + sizeof(uint)];
            var setting = (POWERBROADCAST_SETTING*)buffer;
            setting->PowerSetting = PInvoke.GUID_SESSION_DISPLAY_STATUS;
            setting->DataLength = sizeof(uint);
            *(uint*)&setting->Data = seen ? 1u : unseen == "display dimmed" ? 2u : 0u;
            PInvoke.SendMessage(host.Hwnd, PInvoke.WM_POWERBROADCAST, PInvoke.PBT_POWERSETTINGCHANGE, (nint)setting);
        }
    }
}
