using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Windows.Win32;
using Windows.Win32.System.Power;

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
    // its history and paint nothing, and as soon as somebody can see it again it shows the last one, before any tick.
    [Theory] // Rows: each a message of its own that Windows sends, read apart
    [InlineData("locked")]
    [InlineData("display off")]
    public async Task AnOverlayNobodySeesPaintsNothingAndShowsTheLastReadingOnceSeen(string unseen)
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        var log = new ListLogger(LogLevel.Debug);
        var item = settings.Config.Overlays[0].Items[0].Id;
        var now = HostRun.Recorded;
        var (paints, shown, history) = (-1, new List<string>(), new List<IReadOnlyList<double?>>());

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => now, HostRun.English, _folder.Clock, log, _tray.Environment(_windowMutex));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            shown.Add(Picture(host));
            history.Add(host.History.TenMinutes(item));
            Tell(host, unseen, seen: false);
            var before = log.Entries.Count(entry => entry.Message == "paint");
            now = HostRun.Warmer;
            for (var second = 0; second < 3; second++)
            {
                _folder.Clock.Advance(TimeSpan.FromSeconds(1));
                host.Arrived();
                HostRun.Tick(host);
            }

            // The paint the last tick asked for has its turn too, so only being seen again asks for the next.
            _folder.Clock.Advance(Settings.Interval);
            HostRun.Pump(host);
            paints = log.Entries.Count(entry => entry.Message == "paint") - before;
            shown.Add(Picture(host));
            history.Add(host.History.TenMinutes(item));
            Tell(host, unseen, seen: true);
            _folder.Clock.Advance(Settings.Interval);
            HostRun.Pump(host);
            shown.Add(Picture(host));
        });

        Assert.Equal(0, paints);
        Assert.Equal(shown[0], shown[1]);
        Assert.NotEqual(shown[0], shown[2]);
        Assert.NotEqual(history[0], history[1]);
    }

    private static string Picture(OverlayHost host)
    {
        using var picture = host.Overlays[0].Picture();
        using var png = new MemoryStream();
        picture?.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToBase64String(png.ToArray());
    }

    // As Windows sends them: a lock or unlock of the session, or the session's display turned off or on.
    private static unsafe void Tell(OverlayHost host, string unseen, bool seen)
    {
        if (unseen == "locked")
        {
            PInvoke.SendMessage(host.Hwnd, PInvoke.WM_WTSSESSION_CHANGE, seen ? PInvoke.WTS_SESSION_UNLOCK : PInvoke.WTS_SESSION_LOCK, 0);
            return;
        }

        var buffer = stackalloc byte[sizeof(POWERBROADCAST_SETTING) + sizeof(uint)];
        var setting = (POWERBROADCAST_SETTING*)buffer;
        setting->PowerSetting = PInvoke.GUID_SESSION_DISPLAY_STATUS;
        setting->DataLength = sizeof(uint);
        *(uint*)&setting->Data = seen ? 1u : 0u;
        PInvoke.SendMessage(host.Hwnd, PInvoke.WM_POWERBROADCAST, PInvoke.PBT_POWERSETTINGCHANGE, (nint)setting);
    }
}
