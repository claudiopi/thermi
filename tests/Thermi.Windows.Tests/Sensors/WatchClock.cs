extern alias sensors;

using Microsoft.Extensions.Time.Testing;
using sensors::Thermi.Sensors.Windows.Pipe;

namespace Thermi.Windows.Tests.Sensors;

/// <summary>
/// The host's clock, which tells when the host last read it and whether a write of its pipe is still on its way: a user's
/// host reads it as its watch begins and at each second the watch handles, and nowhere else, its sensors' devices reading
/// the fake clock itself, and arms a timer of <see cref="SensorPipeServer.WriteTimeout"/> for each write, which it
/// disposes once the write ended.
/// </summary>
internal sealed class WatchClock(FakeTimeProvider fake) : TimeProvider
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly Lock _lock = new();
    private long _readAt = long.MinValue;
    private TaskCompletionSource _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _writing;

    public override DateTimeOffset GetUtcNow() => fake.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => fake.LocalTimeZone;

    public override long TimestampFrequency => fake.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = fake.CreateTimer(callback, state, dueTime, period);
        if (dueTime != SensorPipeServer.WriteTimeout || period != Timeout.InfiniteTimeSpan)
        {
            return timer;
        }

        Interlocked.Increment(ref _writing);
        return new WriteTimer(timer, () => Interlocked.Decrement(ref _writing));
    }

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

    /// <summary>Waits until no write the host began is on its way, or its run has <paramref name="ended"/>.</summary>
    public async Task WrittenAsync(Func<bool> ended)
    {
        var start = TimeProvider.System.GetTimestamp();
        while (!ended() && Volatile.Read(ref _writing) > 0)
        {
            Assert.True(TimeProvider.System.GetElapsedTime(start) < Patience, "The host's write never ended.");
            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A write's timeout, which tells once that its write ended as it is disposed.</summary>
    private sealed class WriteTimer(ITimer timer, Action ended) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

        public void Dispose()
        {
            Ended();
            timer.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Ended();
            return timer.DisposeAsync();
        }

        private void Ended()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ended();
            }
        }
    }
}
