extern alias sensors;

using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.SensorHost;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>The service's reads of its devices, one of which stalls, as a board whose bus another program holds.</summary>
public sealed class DeviceUpdatesTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // QA round 4: with a fan controller holding the board's bus under load, the whole snapshot waited for it, 3 to 5.5 s
    // at a time. A device whose update blocks for 5 s holds back none of the others: each second they are read, its last
    // values go out while no older than a snapshot may be, then as missing; it is not started again while it runs, and
    // once it returns it is said to be slow and read again.
    [Fact]
    public async Task ADeviceThatBlocksHoldsBackNoneOfTheOthers()
    {
        var clock = new FakeTimeProvider();
        var blocked = new TaskCompletionSource();
        var (started, said) = (0, new List<(string Name, TimeSpan Took)>());
        var updates = new DeviceUpdates(clock, (name, took, _) => said.Add((name, took)), () => Assert.Fail("No update should hang."));
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
                "Updated Missing Updated",
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
    // one that fails once, which is said with its failure; one that never read is left out, and never said.
    [Fact]
    public async Task ADeviceThatStallsOrFailsKeepsItsLastValuesForThreeSeconds()
    {
        var clock = new FakeTimeProvider();
        var (board, gpu, broken, said) = (0, 0, 0, new List<string>());
        var updates = new DeviceUpdates(clock, (name, _, failed) => said.Add($"{name} {failed?.GetType().Name}"), () => Assert.Fail("No update should hang."));
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

        var seconds = new List<string>();
        for (var second = 0; second < 5; second++)
        {
            var pass = updates.UpdateAsync(devices, TestContext.Current.CancellationToken);
            var waited = pass.IsCompleted ? TimeSpan.Zero : DeviceUpdates.Patience;
            clock.Advance(waited);
            var read = await pass.WaitAsync(Patience, TimeProvider.System, TestContext.Current.CancellationToken);
            seconds.Add(string.Join(' ', devices.Select(device => read[device.Id])));
            clock.Advance(TimeSpan.FromSeconds(1) - waited);
        }

        Assert.Equal(
            ["Updated Updated Failed", "Updated Held Failed", "Held Held Failed", "Updated Held Failed", "Updated Updated Failed"],
            seconds);
        Assert.Equal(["GPU IOException", "Board "], said);
    }
}
