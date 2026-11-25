extern alias sensors;

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using sensors::Thermi.SensorHost;
using Thermi.Overlay;
using Thermi.Windows.Tests.Processes;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>The test that spins every core, alone, after the others: any test beside it would only measure the load.</summary>
[CollectionDefinition(nameof(FullLoadTests), DisableParallelization = true)]
public sealed class AloneUnderLoad;

/// <summary>thermi's whole point is to keep reading while the PC cooks, on the real clock, since a load is real time.</summary>
[Collection(nameof(FullLoadTests))]
public sealed class FullLoadTests
{
    private static readonly TimeSpan Loaded = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Night QA: "Find it" said the readings stopped as its load started. With every core spun by the finder's own load,
    // `thermi cpu-load`, the sensor pipe still gives a snapshot each second, and the load runs at idle priority. The pipe
    // here runs in the test process, below normal under scripts/check.ps1, which is still above the load.
    [Fact]
    public async Task UnderALoadOnEveryCoreTheReadingsKeepComing()
    {
        var clock = TimeProvider.System;
        var pipe = $"thermi-test-{Guid.NewGuid():N}";
        var recorded = TestRecordings.Read(TestRecordings.Admin);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var server = new SensorPipeServer(_ => Task.FromResult(recorded), clock, pipe) { Hung = () => Assert.Fail("No read should hang.") };
        var client = new SensorClient(clock, NullLogger.Instance, new FixedPipe(pipe, _ => true));
        var heard = new List<long>();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Received += (_, _) =>
        {
            lock (heard)
            {
                heard.Add(clock.GetTimestamp());
            }

            first.TrySetResult();
        };
        var serving = server.RunAsync(stop.Token);
        var reading = client.RunAsync(stop.Token);
        await first.Task.WaitAsync(Patience, clock, TestContext.Current.CancellationToken);

        var (priority, busy, from) = await LoadAsync(clock);
        await stop.CancelAsync();
        await Task.WhenAll(serving, reading).WaitAsync(Patience, clock, CancellationToken.None);

        List<long> loaded;
        lock (heard)
        {
            loaded = [.. heard.Where(at => at >= from)];
        }

        var gaps = loaded.Zip(loaded.Skip(1), (one, next) => clock.GetElapsedTime(one, next).TotalSeconds).ToList();
        Assert.Equal(ProcessPriorityClass.Idle, priority);
        Assert.True(busy >= Environment.ProcessorCount * Loaded.TotalSeconds * 0.5, $"The load kept the cores busy for {busy:F1} s.");
        Assert.True(loaded.Count >= Loaded.TotalSeconds - 1 && gaps.Max() < 2, $"{loaded.Count} snapshots, gaps {string.Join(", ", gaps)}.");
    }

    // The fan finder's own load, as the window starts it, run to its end: its priority class while it runs, how long it
    // kept the cores busy, and when it started.
    private static async Task<(ProcessPriorityClass Priority, double Busy, long From)> LoadAsync(TimeProvider clock)
    {
        using var load = DesktopProcess.Start(Path.Combine(AppContext.BaseDirectory, "thermi.exe"), ["cpu-load", $"{Loaded.TotalSeconds}"]);
        var from = clock.GetTimestamp();
        using var watched = Process.GetProcessById(load.Id);
        await Task.Delay(Loaded / 2, clock, TestContext.Current.CancellationToken);
        var priority = watched.PriorityClass;
        Assert.True(await Task.Run(() => load.WaitForExit(Loaded + Patience)), "The load never ended.");
        return (priority, watched.TotalProcessorTime.TotalSeconds, from);
    }
}
