using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using Thermi.Core.Sensors;
using Thermi.SensorHost.Frames;

namespace Thermi.SensorHost;

/// <summary>What the sensors read now: LibreHardwareMonitor's, or in tests a recording's.</summary>
internal interface ISensorSource : IDisposable
{
    Task<Snapshot> ReadAsync(CancellationToken stop);
}

/// <summary>
/// What <c>thermi-sensors</c> reaches outside its own code, which tests replace with their own: the sensors, opened only
/// once the arguments make sense, told where to say a device that updates slowly or fails and how long to wait for each
/// device, the clock, the terminal it answers in, the log its services write to, the pipe it serves, and the traces of presents.
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
        Trace = () => new PresentTrace(TimeProvider.System),
        Recording = () => new PresentTrace(TimeProvider.System, PresentTrace.RecordingName),
    };

    /// <summary>What tells the service of the presents games make, for their FPS; without it, the service serves no frames.</summary>
    public Func<IPresentTrace>? Trace { get; init; }

    /// <summary>What tells <c>--frames</c> of them, in a session of its own; without it, <see cref="Trace"/>.</summary>
    public Func<IPresentTrace>? Recording { get; init; }

    public string PipeName { get; init; } = SnapshotJson.PipeName;
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
