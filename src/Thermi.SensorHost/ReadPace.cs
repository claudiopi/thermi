using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>
/// When a kind of hardware is updated, and whether the speeds of its last update are speeds now. The library's speeds are
/// the data moved since its previous update over the time since: after a gap past the overlay's stale limit beyond the
/// pace, as across sleep or while no client listened, that is the gap's average rather than a speed now. The wall clock,
/// since it counts the time asleep.
/// </summary>
internal sealed class ReadPace(TimeProvider time, TimeSpan every)
{
    private DateTimeOffset? _last;

    /// <summary>Whether the speeds of the last update hold; the first update's do, as the library starts them.</summary>
    public bool SpeedsHold { get; private set; } = true;

    /// <summary>
    /// Whether to update now: at the first read, once its pace has passed, and at once after an update whose
    /// speeds did not hold, so speeds come back at the next read instead of a whole pace later.
    /// </summary>
    public bool Due => _last is not { } last || !SpeedsHold || time.GetUtcNow() - last >= every;

    public void Updated()
    {
        var now = time.GetUtcNow();
        SpeedsHold = _last is not { } last || now - last <= every + LatestSnapshot.MaxAge;
        _last = now;
    }
}
