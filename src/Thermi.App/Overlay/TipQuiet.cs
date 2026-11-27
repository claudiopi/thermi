using Thermi.App.Gestures;

namespace Thermi.App.Overlay;

/// <summary>What holds an overlay's tooltips quiet.</summary>
[Flags]
internal enum Hush
{
    None = 0,
    Press = 1,
    Menu = 2,
    Drop = 4,
}

/// <summary>
/// What holds an overlay's value tooltips quiet on every OS: a press or a drag on it, its menu, which a tooltip would
/// cover, and the quiet after a drop, until none of them does, so the quiet after a drop ending mid-press, or a menu
/// closing within it, frees nothing the others hold.
/// </summary>
internal sealed class TipQuiet
{
    // The drop whose quiet holds the tooltips, while it does, and the last one heard of, which holds them only once.
    private DropQuiet? _quiet;
    private long? _heard;
    private Hush _hush;

    /// <summary>Whether a tooltip may show now.</summary>
    public bool Speaks => _hush == Hush.None;

    public void Quiet(Hush why, bool quiet) => _hush = quiet ? _hush | why : _hush & ~why;

    /// <summary>
    /// The overlay shows after <paramref name="drop"/>: while its quiet lasts, a drop not yet heard of holds the tooltips
    /// until the pointer leaves the overlay or <see cref="Woke"/> finds it over. The wait until then, or null when it
    /// holds nothing.
    /// </summary>
    public TimeSpan? After(DropQuiet drop)
    {
        if (drop.At == _heard || drop.Left <= TimeSpan.Zero)
        {
            return null;
        }

        (_heard, _quiet) = (drop.At, drop);
        Quiet(Hush.Drop, true);
        return drop.Left;
    }

    /// <summary>The wait <see cref="After"/> gave is over: what is left of the quiet, or null once the tooltips may show again.</summary>
    public TimeSpan? Woke()
    {
        if (_quiet is { Left: var left } && left > TimeSpan.Zero)
        {
            return left;
        }

        Speak();
        return null;
    }

    /// <summary>The pointer left the overlay, which ends a drop's quiet: true when one held the tooltips.</summary>
    public bool Left()
    {
        if (_quiet is null)
        {
            return false;
        }

        Speak();
        return true;
    }

    private void Speak()
    {
        _quiet = null;
        Quiet(Hush.Drop, false);
    }
}
