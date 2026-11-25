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
        var updates = new DeviceUpdates(clock, (name, took) => said.Add((name, took)), () => Assert.Fail("No update should hang."));
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
}
