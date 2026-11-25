extern alias sensors;

using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>The service's reads of its devices, one of which stalls, as a board whose bus another program holds.</summary>
public sealed class DeviceUpdatesTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // QA round 4: with a fan controller holding the board's bus under load, the whole snapshot waited for it, 3 to 5.5 s
    // at a time. A device whose update blocks for 5 s holds back none of the others: each second they are read, its last
    // values go out for 3 s from when its update came due, then as missing; it is not started again while it runs, and
    // once it returns it is said to be slow and read again.
    [Fact]
    public async Task ADeviceThatBlocksHoldsBackNoneOfTheOthers()
    {
        var clock = new FakeTimeProvider();
        var blocked = new TaskCompletionSource();
        var (started, said) = (0, new List<(string Name, TimeSpan Took)>());
        var updates = new DeviceUpdates(clock, (name, took, _) => said.Add((name, took)), () => Assert.Fail("No update should hang."), DeviceUpdates.Patience);
        DeviceUpdate[] devices =
        [
            new("/cpu", "CPU", () => Task.CompletedTask, Due: true),
            new("/board", "Board", () => ++started == 1 ? Task.CompletedTask : blocked.Task, Due: true),
            new("/gpu", "GPU", () => Task.CompletedTask, Due: true),
        ];

        var seconds = new List<string>();
        for (var second = 0; second < 7; second++)
        {
            var pass = updates.UpdateAsync(devices, TestContext.Current.CancellationToken);
            // A pass waits for the board on the fake clock; the real one only bounds the test.
            clock.Advance(pass.IsCompleted ? TimeSpan.Zero : DeviceUpdates.Patience);
            var read = await pass.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
            seconds.Add(string.Join(' ', devices.Select(device => read[device.Id])));
            clock.Advance(TimeSpan.FromSeconds(second == 5 ? 0.5 : 1));
            if (second == 5)
            {
                blocked.SetResult();
                clock.Advance(TimeSpan.FromSeconds(0.5));
            }
        }

        Assert.Equal(
            [
                "Updated Updated Updated",
                "Updated Held Updated",
                "Updated Held Updated",
                "Updated Held Updated",
                "Updated Missing Updated",
                "Updated Missing Updated",
                "Updated Updated Updated",
            ],
            seconds);
        Assert.Equal(3, started);
        Assert.Equal("Board", Assert.Single(said).Name);
        Assert.InRange(said[0].Took, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6));
    }

    // QA round 5: under Find it's load the CPU's update took 1.1 s, and its items went blank for a second at a time, 5 s
    // apart, long before 3 s: a busy driver answers late, or not at all, and an update that failed left the device out of
    // that second's snapshot. A device that stalls 1.5 s and then fails at once keeps its last values all along, as does
    // one that fails once, which is said with its failure; one that never read is left out, and never said. A drive, read
    // every 5 s, keeps its values between its reads; one whose read stalls and fails, and fails again 5 s later, keeps
    // them for 3 s from when that read came due, then sends them as missing, and its second failure, within a minute of
    // the first, is not said.
    [Fact]
    public async Task ADeviceThatStallsOrFailsKeepsItsLastValuesForThreeSeconds()
    {
        var clock = new FakeTimeProvider();
        var (board, gpu, broken, drive, said) = (0, 0, 0, 0, new List<string>());
        var updates = new DeviceUpdates(
            clock, (name, _, failed) => said.Add($"{name} {failed?.GetType().Name}"), () => Assert.Fail("No update should hang."), DeviceUpdates.Patience);
        DeviceUpdate[] devices =
        [
            new("/gpu", "GPU", () => ++gpu == 3 ? Task.FromException(new IOException("The driver reset.")) : Task.CompletedTask, Due: true),
            new("/board", "Board", () => ++board switch
            {
                2 => Task.Delay(TimeSpan.FromSeconds(1.5), clock),
                3 => Task.FromException(new IOException("The driver answered nothing.")),
                _ => Task.CompletedTask,
            }, Due: true),
            new("/broken", "Broken", () => ++broken > 0 ? Task.FromException(new IOException("No such device.")) : Task.CompletedTask, Due: true),
        ];
        var stalls = new DeviceUpdate("/drive", "Drive", () => ++drive == 1 ? Task.CompletedTask : StallsAndFails(clock), Due: false);

        var seconds = new List<string>();
        for (var second = 0; second < 11; second++)
        {
            DeviceUpdate[] read = [.. devices, stalls with { Due = second % 5 == 0 }];
            var pass = updates.UpdateAsync(read, TestContext.Current.CancellationToken);
            var waited = pass.IsCompleted ? TimeSpan.Zero : DeviceUpdates.Patience;
            clock.Advance(waited);
            var each = await pass.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
            seconds.Add(string.Join(' ', read.Select(device => each[device.Id])));
            clock.Advance(TimeSpan.FromSeconds(1) - waited);
        }

        Assert.Equal(
            [
                "Updated Updated Failed Updated",
                "Updated Held Failed Held",
                "Held Held Failed Held",
                "Updated Held Failed Held",
                "Updated Updated Failed Held",
                "Updated Updated Failed Held",
                "Updated Updated Failed Held",
                "Updated Updated Failed Held",
                "Updated Updated Failed Held",
                "Updated Updated Failed Missing",
                "Updated Updated Failed Missing",
            ],
            seconds);
        Assert.Equal(["GPU IOException", "Board ", "Drive IOException"], said);

        static async Task StallsAndFails(TimeProvider clock)
        {
            await Task.Delay(TimeSpan.FromSeconds(0.7), clock);
            throw new IOException("The drive did not answer.");
        }
    }

    // The library's update blocks the thread it runs on, as one waiting on a bus does: the next devices' run on another
    // thread while it waits, and none on the pool's, which a stall as long as the read limit would starve.
    [Fact]
    public async Task AStalledUpdateHoldsOnlyAThreadOfItsOwn()
    {
        using var threads = new UpdateThreads();
        using var release = new ManualResetEventSlim();
        var pooled = new ConcurrentQueue<bool>();
        var token = TestContext.Current.CancellationToken;

        var stalled = threads.Run(() =>
        {
            pooled.Enqueue(Thread.CurrentThread.IsThreadPoolThread);
            release.Wait(token);
        });
        await threads.Run(() => pooled.Enqueue(Thread.CurrentThread.IsThreadPoolThread)).WaitAsync(Patience, TimeProvider.System, token);
        await threads.Run(() => pooled.Enqueue(Thread.CurrentThread.IsThreadPoolThread)).WaitAsync(Patience, TimeProvider.System, token);
        release.Set();
        await stalled.WaitAsync(Patience, TimeProvider.System, token);

        Assert.Equal([false, false, false], pooled);
    }

    // A stop that comes while an update still runs leaves it running, and the library open under it, as closing or
    // scanning it again would pull the device from under the update; one still running past the read limit ends the process.
    [Fact]
    public async Task AnUpdateStillRunningKeepsTheLibraryOpenAndPastTheReadLimitEndsTheProcess()
    {
        var clock = new FakeTimeProvider();
        using var blocked = new CancellationTokenSource();
        var hung = 0;
        var updates = new DeviceUpdates(clock, (_, _, _) => { }, () => hung++, DeviceUpdates.Patience);
        DeviceUpdate[] devices =
        [
            new("/board", "Board", () => Task.Delay(Timeout.InfiniteTimeSpan, clock, blocked.Token), Due: true),
            new("/gpu", "GPU", () => Task.CompletedTask, Due: true),
        ];
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(0.1), clock);

        var stopped = Assert.ThrowsAnyAsync<OperationCanceledException>(() => updates.UpdateAsync(devices, stop.Token));
        clock.Advance(TimeSpan.FromSeconds(0.1));
        await stopped;
        var busy = updates.Busy;
        clock.Advance(SensorPipeServer.ReadLimit - TimeSpan.FromSeconds(0.1));
        await Read();
        var atTheLimit = hung;
        clock.Advance(TimeSpan.FromSeconds(1));
        await Read();
        var past = hung;
        await blocked.CancelAsync();
        await Read();

        Assert.Equal((true, 0, 1, false), (busy, atTheLimit, past, updates.Busy));

        // A pass that waits on the fake clock would wait for ever; the real one only bounds the test.
        Task Read()
        {
            var token = TestContext.Current.CancellationToken;
            return updates.UpdateAsync(devices, token).WaitAsync(Patience, TimeProvider.System, token);
        }
    }
}
