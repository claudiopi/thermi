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
/// device, the clock, the pipe it serves and the terminal it answers in.
/// </summary>
internal sealed record SensorHostEnvironment(
    Func<Action<string, TimeSpan, Exception?>, TimeSpan, ISensorSource> Open, TimeProvider Time, TextWriter Out, TextWriter Error)
{
    public static SensorHostEnvironment Default { get; } =
        new((slow, patience) => new LhmSensorSource(TimeProvider.System, slow, patience), TimeProvider.System, Console.Out, Console.Error);

    public string PipeName { get; init; } = SnapshotJson.PipeName;

    /// <summary>Where the user's host logs (docs/adr/0016-admin-on-demand.md).</summary>
    public string UserLog { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "thermi", "thermi-sensors.log");
}
