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
/// device, the clock, the pipe it serves and the terminal it answers in.
/// </summary>
internal sealed record SensorHostEnvironment(
    Func<Action<string, TimeSpan, Exception?>, TimeSpan, ISensorSource> Open, TimeProvider Time, TextWriter Out, TextWriter Error)
{
    public static SensorHostEnvironment Default { get; } =
        new((slow, patience) => new LhmSensorSource(TimeProvider.System, slow, patience), TimeProvider.System, Console.Out, Console.Error)
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
