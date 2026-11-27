using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Placement;
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

    // High contrast is read once and kept until Windows says a setting or a system color changed, whatever paints between.
    [Theory] // Rows: each a message of its own the host reads apart
    [InlineData(0u, 0)]
    [InlineData(PInvoke.WM_SETTINGCHANGE, 1)]
    [InlineData(PInvoke.WM_SYSCOLORCHANGE, 1)]
    public async Task HighContrastIsReadAgainOnlyOnceWindowsSaysItChanged(uint message, int again)
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        var reads = 0;
        var environment = _tray.Environment(_windowMutex) with { Reads = () => new DesktopReads(() => { reads++; return null; }) };
        var counted = (Painting: -1, After: -1);

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => HostRun.Recorded, HostRun.English, _folder.Clock, _folder.Log, environment);
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            var first = reads;
            Seconds(host, 3, () => { });
            counted.Painting = reads - first;
            if (message != 0)
            {
                // Sent, not posted: Windows refuses to post a setting change, whose lParam may point to a string.
                PInvoke.SendMessage(host.Hwnd, message, 0, 0);
            }

            Seconds(host, 1, () => { });
            counted.After = reads - first - counted.Painting;
        });

        Assert.Equal((0, again), counted);
    }

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

    /// <summary>The session as a test has it: away or not, and the window in front, which a lock takes away.</summary>
    private sealed class Sight
    {
        private bool _away;

        public ForegroundWindow? Front { get; set; }

        public HostEnvironment Of(HostEnvironment environment) =>
            environment with { Foreground = () => Front, Sight = FakeTray.Watched with { Away = () => _away } };

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
