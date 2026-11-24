using Thermi.Core.Sensors;

namespace Thermi.SensorHost;

/// <summary>What the sensors read now: LibreHardwareMonitor's, or in tests a recording's.</summary>
internal interface ISensorSource : IDisposable
{
    Snapshot Read();
}

/// <summary>
/// What <c>thermi-sensors</c> reaches outside its own code, which tests replace with their own: the sensors, opened only
/// once the arguments make sense, the clock, the pipe it serves and the terminal it answers in.
/// </summary>
internal sealed record SensorHostEnvironment(Func<ISensorSource> Open, TimeProvider Time, TextWriter Out, TextWriter Error)
{
    public static SensorHostEnvironment Default { get; } = new(() => new LhmSensorSource(TimeProvider.System), TimeProvider.System, Console.Out, Console.Error);

    public string PipeName { get; init; } = SnapshotJson.PipeName;
}
