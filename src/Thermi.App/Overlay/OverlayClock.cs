using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.App.Overlay;

/// <summary>
/// An overlay's own clock on every OS, past its readings', on the injected clock: how long it has run, which OLED care
/// moves it by, and its critical glow, whose frames come about 30 times a second only while it glows. OLED care steps
/// on the readings' paint each second, as Windows' does, so it needs no wake of its own.
/// </summary>
internal sealed class OverlayClock(TimeProvider time)
{
    /// <summary>The time between two of a glow's frames.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    private readonly long _started = time.GetTimestamp();
    private readonly CriticalGlow _glow = new(time);

    /// <summary>How long the overlay has run, which <see cref="Snaps.Locate"/> moves it by with OLED care on.</summary>
    public TimeSpan Running => time.GetElapsedTime(_started);

    public bool Glowing => _glow.Glowing;

    /// <summary>
    /// Takes the items about to be painted, of which one critical for the first time starts its glow when
    /// <paramref name="animate"/>: how strongly each item's pill glows now, or null while none does.
    /// </summary>
    public Func<string, double>? Glow(IEnumerable<OverlayItem> items, bool animate)
    {
        _glow.Update(items, animate);
        return _glow.Glowing ? _glow.Of : null;
    }
}
