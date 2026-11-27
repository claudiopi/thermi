using System.Globalization;

namespace Thermi.App.Logging;

/// <summary>
/// The events <c>--trace</c> writes on standard error for what waits on them, a line each, as
/// <c>812.034 window-shown cpu.load</c>: seconds on the system's monotonic clock, which every process of the machine reads
/// alike, the event, and what it is about, when anything. Any other line is thermi's own log.
/// </summary>
internal sealed class TraceEvents(TextWriter writer, TimeProvider time)
{
    /// <summary>The settings window showed or came to the front, about the item it shows, or about nothing.</summary>
    public const string WindowShown = "window-shown";

    public void Write(string name, string? about = null)
    {
        var at = (double)time.GetTimestamp() / time.TimestampFrequency;
        var line = string.IsNullOrEmpty(about) ? name : $"{name} {about}";
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{at:F3} {line}"));
    }
}
