using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>What the sensors read now: LibreHardwareMonitor's, or in tests a recording's.</summary>
internal interface ISensorSource : IDisposable
{
    Task<Snapshot> ReadAsync(CancellationToken stop);
}

/// <summary>
/// What <c>thermi-sensors</c> reaches outside its own code, which tests replace with their own: the sensors, opened only
/// once the arguments make sense, told where to say a device that updates slowly or fails and how long to wait for each
/// device, the clock, the terminal it answers in, the log its services write to, and the pipe it serves.
/// </summary>
internal sealed record SensorHostEnvironment(
    Func<Action<string, TimeSpan, Exception?>, TimeSpan, ISensorSource> Open,
    TimeProvider Time,
    TextWriter Out,
    TextWriter Error,
    Action<string, EventLogEntryType> Log)
{
    public static SensorHostEnvironment Default { get; } = new(
        (slow, patience) => new LhmSensorSource(TimeProvider.System, slow, patience),
        TimeProvider.System,
        Console.Out,
        Console.Error,
        ApplicationLog.Write)
    {
        SessionEnds = User.SessionEnd.Watch,
    };

    public string PipeName { get; init; } = SnapshotJson.PipeName;

    /// <summary>
    /// Tells the user's host the session ends, through a hidden window, and where to say the window could not be made; none
    /// in tests, which show no window.
    /// </summary>
    public Func<Action, Action<string>, IDisposable>? SessionEnds { get; init; }

    /// <summary>Where the user's host logs (docs/adr/0016-admin-on-demand.md).</summary>
    public string UserLog { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "thermi", "thermi-sensors.log");
}

/// <summary>Windows' Application log, under the sensor service's source, which the installer registers and removes.</summary>
internal static class ApplicationLog
{
    /// <summary>Writes <paramref name="message"/>, which the log may refuse: what the service does next matters more.</summary>
    public static void Write(string message, EventLogEntryType type)
    {
        try
        {
            EventLog.WriteEntry(SensorService.Name, message, type);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or SecurityException)
        {
            // Nowhere else to report it.
        }
    }
}
