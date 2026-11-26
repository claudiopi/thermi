namespace Thermi.App.Gestures;

/// <summary>
/// The quiet after a drop, shared by every overlay: the pointer is left on a value of the overlay dragged, of the one it
/// joined or of the one it made, whose tooltip would show at once, so none shows until the pointer leaves that overlay or
/// <see cref="Lasts"/> passes.
/// </summary>
internal sealed class DropQuiet(TimeProvider time)
{
    /// <summary>How long after a drop no tooltip shows while the pointer stays on the overlay.</summary>
    public static readonly TimeSpan Lasts = TimeSpan.FromSeconds(1.5);

    /// <summary>When the last drop let go, on the injected clock, or null before any.</summary>
    public long? At { get; private set; }

    /// <summary>How much of the quiet after the last drop is left, zero once it is over.</summary>
    public TimeSpan Left => At is { } at && time.GetElapsedTime(at) is var since && since < Lasts ? Lasts - since : TimeSpan.Zero;

    public void Dropped() => At = time.GetTimestamp();
}
