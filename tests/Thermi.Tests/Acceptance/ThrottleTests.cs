using Microsoft.Extensions.Time.Testing;
using Thermi.Core;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Window writes and overlay reloads go through one throttle on the injected clock.
/// </summary>
public sealed class ThrottleTests
{
    [Fact]
    public void AThrottleLetsOneChangeOutEveryHundredMillisecondsAndAlwaysTheLast()
    {
        var time = new FakeTimeProvider();
        var start = time.GetTimestamp();
        var sent = new List<(TimeSpan At, int Value)>();
        var throttle = new Throttle<int>(TimeSpan.FromMilliseconds(100), value => sent.Add((time.GetElapsedTime(start), value)), time);

        for (var value = 1; value <= 20; value++)
        {
            throttle.Post(value);
            time.Advance(TimeSpan.FromMilliseconds(30));
        }

        time.Advance(TimeSpan.FromMilliseconds(100));
        var quiet = time.GetElapsedTime(start);
        throttle.Post(21);
        throttle.Post(22);
        var beforeClosing = sent.ToList();
        throttle.Dispose();

        // Once closed there is no timer to wait on, so a late value goes out at once rather than never.
        throttle.Post(23);
        throttle.Post(24);
        throttle.Dispose();

        Assert.Equal((TimeSpan.Zero, 1), sent[0]);
        Assert.All(beforeClosing.Zip(beforeClosing.Skip(1)), pair => Assert.True(pair.Second.At - pair.First.At >= TimeSpan.FromMilliseconds(100), $"{pair.First} then {pair.Second}"));
        Assert.Equal([(quiet, 21), (quiet, 22), (quiet, 23), (quiet, 24)], sent[^4..]);
        Assert.Contains(20, sent.Select(change => change.Value));
    }
}
