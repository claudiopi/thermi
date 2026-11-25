using Microsoft.Extensions.Time.Testing;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// A fake clock that says when the code under test waits on it: each timer made is the updater idle until its next tick,
/// so a test steps the clock only once the work of the last tick is done, and nothing races.
/// </summary>
internal sealed class WaitingClock(DateTimeOffset start) : TimeProvider, IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _fake = new(start);
    private readonly SemaphoreSlim _waiting = new(0);

    public override DateTimeOffset GetUtcNow() => _fake.GetUtcNow();

    public override long GetTimestamp() => _fake.GetTimestamp();

    public override long TimestampFrequency => _fake.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _fake.CreateTimer(callback, state, dueTime, period);
        _waiting.Release();
        return timer;
    }

    /// <summary>Waits until the code waits on a timer, then steps the clock by <paramref name="step"/>.</summary>
    public async Task StepAsync(TimeSpan step)
    {
        Assert.True(await _waiting.WaitAsync(Patience, TestContext.Current.CancellationToken), "The updater never waited on its clock.");
        _fake.Advance(step);
    }

    /// <summary>Steps the clock at once, as within a wait already counted.</summary>
    public void Advance(TimeSpan step) => _fake.Advance(step);

    /// <summary>Steps a minute at a time, the updater's tick, through <paramref name="span"/>.</summary>
    public async Task PassAsync(TimeSpan span)
    {
        for (var passed = TimeSpan.Zero; passed < span; passed += TimeSpan.FromMinutes(1))
        {
            await StepAsync(TimeSpan.FromMinutes(1));
        }
    }

    /// <summary>Waits until the work of the last step is done, the code waiting on its clock again.</summary>
    public async Task SettledAsync()
    {
        Assert.True(await _waiting.WaitAsync(Patience, TestContext.Current.CancellationToken), "The updater never waited on its clock.");
        _waiting.Release();
    }

    public void Dispose() => _waiting.Dispose();
}
