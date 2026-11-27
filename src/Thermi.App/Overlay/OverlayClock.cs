using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.App.Overlay;

/// <summary>
/// An overlay's own clock on every OS, past its readings', on the injected clock: how long it has run, which OLED care
/// moves it by, and its critical glow, whose frames come about 30 times a second only while it glows.
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

    /// <summary>
    /// When the overlay paints again for its own clock alone, for a host that paints only when something changed: the
    /// glow's next frame, or the next step of <paramref name="overlay"/>'s OLED care, or null for neither.
    /// </summary>
    public TimeSpan? Next(OverlayConfig overlay) =>
        _glow.Glowing ? Frame
        : overlay.Oled ? OledCare.Step - TimeSpan.FromTicks(Running.Ticks % OledCare.Step.Ticks)
        : null;
}
