using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Thermi.App.Window;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Tests.Agents;

namespace Thermi.Tests.Bridge;

/// <summary>What the window says of a CPU read without thermi's driver, on each OS's desktop.</summary>
public sealed class DriverTests
{
    // A CPU read without admin rights reads zeros. On Windows the catalog's rows, the CPU temperature's chart and the fan
    // finder say thermi's driver may be missing; Linux's kernel reads a CPU with no driver of thermi's, so there none says it.
    [Theory] // Rows: each OS.
    [InlineData(HostOs.Windows, true)]
    [InlineData(HostOs.Linux, false)]
    public async Task OnlyADesktopThatOffersTheDriverSaysItMayBeMissing(HostOs os, bool said)
    {
        using var folder = new SettingsFolder();
        await using var settings = await folder.LoadedAsync(ThermiConfig.New);
        var feed = new RecordedFeed([TestRecordings.Read(TestRecordings.NoAdmin)]);
        using var stop = new CancellationTokenSource();
        _ = feed.RunAsync(stop.Token);
        var start = new PageStart(1, Texts.English, folder.Clock, feed, folder.File, NullLogger.Instance) { Load = _ => { } };
        var turn = new ConcurrentQueue<Action>();
        using var bridge = new PageBridge(
            new DesktopHost(new HostDesktop(os)), start, new WindowEditor(settings, turn.Enqueue, folder.Log, folder.Clock), new ChartHistory(folder.Clock), turn.Enqueue);
        bridge.Arrived();
        while (turn.TryDequeue(out var action))
        {
            action();
        }

        var catalog = await bridge.Catalog("", [], all: true);
        var chart = await bridge.Chart(Metrics.CpuTemperature.Id);
        var finder = await bridge.Finder();
        await stop.CancelAsync();

        Assert.Equal(
            (said, said, said),
            (catalog.Groups.Any(group => group.Sensors.Any(row => row.Why == NoValue.DriverMayBeMissing)),
                chart?.Empty == ChartEmpty.Driver,
                finder.Refusal == FanFinderRefusal.DriverMayBeMissing));
    }
}
