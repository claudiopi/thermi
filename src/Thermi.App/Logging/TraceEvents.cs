using System.Globalization;

namespace Thermi.App.Logging;

/// <summary>
/// What <c>--trace</c> writes on the standard error for a test to wait on, one line each: the monotonic clock's seconds,
/// to the millisecond, the event, and what it is about, if anything. Any other line there is the log's, and no event.
/// Without <c>--trace</c> it writes nothing.
/// </summary>
internal sealed class TraceEvents(TimeProvider time, TextWriter? writer)
{
    /// <summary>A second of the service's taken and painted, whether an overlay is on screen or not.</summary>
    public const string Paint = "paint";

    /// <summary>
    /// Every overlay not hidden is on screen as painted: at start, once they show the service's first second or that it is
    /// stopped, and each time they come back after <see cref="Hidden"/>.
    /// </summary>
    public const string Shown = "shown";

    /// <summary>"Hide overlays" or the hotkey took the overlays off the screen.</summary>
    public const string Hidden = "hidden";

    /// <summary>One change of the settings is saved and on screen, wherever it came from.</summary>
    public const string EditApplied = "edit-applied";

    /// <summary>A drag or a hold ended, let go or taken back, and what it left is saved and on screen.</summary>
    public const string Dropped = "dropped";

    /// <summary>A hold lifted a pill or an item, which shows where the pointer carries it.</summary>
    public const string Lifted = "lifted";

    /// <summary>An overlay with Fade near the pointer shows at its faded look.</summary>
    public const string Faded = "faded";

    private readonly Lock _lock = new();

    public TimeProvider Time => time;

    /// <summary>Whether it writes: a process it starts is then traced too, on the same standard error.</summary>
    public bool On => writer is not null;

    public void Write(string name, string? about = null)
    {
        if (writer is null)
        {
            return;
        }

        var seconds = (double)time.GetTimestamp() / time.TimestampFrequency;
        var line = string.Create(CultureInfo.InvariantCulture, $"{seconds:F3} {name}{(about is null ? "" : " " + about)}");
        lock (_lock)
        {
            writer.WriteLine(line);
            writer.Flush();
        }
    }
}
