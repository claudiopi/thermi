using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>
/// Records a series for tests that replay time, like the fan finder's: one snapshot per second, one line
/// each, in the pipe's format, so a replay reads it exactly as the overlay reads the pipe.
/// </summary>
internal static class SeriesRecorder
{
    public static async Task RecordAsync(Func<Snapshot> read, TextWriter writer, int seconds, TimeProvider time, CancellationToken cancel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        for (var second = 0; second < seconds; second++)
        {
            if (second > 0)
            {
                await tick.WaitForNextTickAsync(cancel);
            }

            await writer.WriteLineAsync(SnapshotJson.Write(read()).AsMemory(), cancel);
            await writer.FlushAsync(cancel);
        }
    }
}
