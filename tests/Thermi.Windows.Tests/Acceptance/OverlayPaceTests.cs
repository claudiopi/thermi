using Microsoft.Extensions.Logging;
using Thermi.Windows.Overlay;

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
}
