using Microsoft.Extensions.Time.Testing;

namespace Thermi.Windows.Tests.Sensors;

/// <summary>
/// The host's clock, which tells when the host last read it: a user's host reads it as its watch begins and at each
/// second the watch handles, and nowhere else, its sensors' devices reading the fake clock itself.
/// </summary>
internal sealed class WatchClock(FakeTimeProvider fake) : TimeProvider
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private long _readAt = long.MinValue;
    private TaskCompletionSource _read = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override DateTimeOffset GetUtcNow() => fake.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => fake.LocalTimeZone;

    public override long TimestampFrequency => fake.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        fake.CreateTimer(callback, state, dueTime, period);

    public override long GetTimestamp()
    {
        TaskCompletionSource read;
        lock (_lock)
        {
            _readAt = fake.GetUtcNow().UtcTicks;
            (read, _read) = (_read, new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        read.TrySetResult();
        return fake.GetTimestamp();
    }

    /// <summary>Waits until the host has read the clock at this second, or its run has <paramref name="ended"/>.</summary>
    public async Task HandledAsync(Func<bool> ended)
    {
        var start = TimeProvider.System.GetTimestamp();
        while (true)
        {
            Task read;
            lock (_lock)
            {
                if (ended() || _readAt == fake.GetUtcNow().UtcTicks)
                {
                    return;
                }

                read = _read.Task;
            }

            // A run that ends reads the clock no more, so it is looked at again every moment.
            Assert.True(TimeProvider.System.GetElapsedTime(start) < Patience, "The host never handled its second.");
            await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, TestContext.Current.CancellationToken));
        }
    }
}
