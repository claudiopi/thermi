using Thermi.Core.Sensors;

namespace Thermi.Core.Pipe;

/// <summary>
/// Records a series for tests that replay time, like the fan finder's: one snapshot per second, one line
/// each, whole, as the pipe sends a snapshot to a client that has not had its sensors, so a replay reads it as the overlay does.
/// </summary>
public static class SeriesRecorder
{
    public static async Task RecordAsync(
        Func<CancellationToken, Task<Snapshot>> read, TextWriter writer, int seconds, TimeProvider time, CancellationToken cancel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        for (var second = 0; second < seconds; second++)
        {
            if (second > 0)
            {
                await tick.WaitForNextTickAsync(cancel);
            }

            await writer.WriteLineAsync(SnapshotJson.Write(await read(cancel)).AsMemory(), cancel);
            await writer.FlushAsync(cancel);
        }
    }
}
