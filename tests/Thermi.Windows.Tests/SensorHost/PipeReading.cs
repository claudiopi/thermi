using System.IO.Pipes;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>A test's client of the sensor host's pipe, which steps the host's fake clock while it waits.</summary>
internal static class PipeReading
{
    /// <summary>
    /// Ticks the clock until a whole snapshot arrives; the server may not have registered the client yet. Time moves only
    /// while nothing arrives, so reading a long line never looks like a stuck client.
    /// </summary>
    public static async Task<Snapshot> NextSnapshotAsync(NamedPipeClientStream client, FakeTimeProvider clock, TimeSpan patience)
    {
        var lines = new SnapshotLines();
        var buffer = new byte[64 * 1024];
        using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        giveUp.CancelAfter(patience);
        var idle = true;
        while (true)
        {
            if (idle)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
            }

            idle = true;
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(giveUp.Token);
            wait.CancelAfter(TimeSpan.FromMilliseconds(100));
            try
            {
                var read = await client.ReadAsync(buffer, wait.Token);
                Assert.NotEqual(0, read);
                idle = false;
                if (lines.Add(buffer.AsSpan(0, read)) is [var snapshot, ..])
                {
                    return snapshot;
                }
            }
            catch (OperationCanceledException) when (!giveUp.IsCancellationRequested)
            {
                // Nothing yet: tick again.
            }
        }
    }
}
