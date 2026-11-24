using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// The glow a pill gives once, the first time one of its items goes critical: up and back down over <see cref="Length"/>,
/// on the injected clock. An item glows once per run of the overlay, and never while Windows' animation effects are off,
/// not even later, so turning them on never brings back an old glow.
/// </summary>
public sealed class CriticalGlow(TimeProvider time)
{
    public static readonly TimeSpan Length = TimeSpan.FromMilliseconds(600);

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _started = new(StringComparer.Ordinal);

    /// <summary>True while a glow runs, so the overlay paints its frames.</summary>
    public bool Glowing => _started.Count > 0;

    /// <summary>Takes the items about to be painted: one critical for the first time starts its glow when <paramref name="animate"/>.</summary>
    public void Update(IEnumerable<OverlayItem> items, bool animate)
    {
        foreach (var item in items.Where(item => item.Level == Level.Critical && item.Id.Length > 0))
        {
            if (_seen.Add(item.Id) && animate)
            {
                _started[item.Id] = time.GetTimestamp();
            }
        }

        foreach (var ended in _started.Where(glow => time.GetElapsedTime(glow.Value) >= Length).Select(glow => glow.Key).ToList())
        {
            _started.Remove(ended);
        }
    }

    /// <summary>How strongly the item's pill glows now, from 0 to 1 and back.</summary>
    public double Of(string itemId) =>
        _started.TryGetValue(itemId, out var start) && time.GetElapsedTime(start) is var elapsed && elapsed < Length
            ? Math.Sin(Math.PI * (elapsed / Length))
            : 0;
}
