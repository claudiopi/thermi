using Microsoft.Extensions.Logging;
using Thermi.App.Sensors;
using Thermi.Core.Sensors;
using Thermi.MacOS.Cli.Native;

namespace Thermi.MacOS.Cli.Sensors;

/// <summary>
/// Reads <c>thermi-sensors</c>' socket on a Mac (<see cref="SensorReader"/>): the person's service's, which launchd holds in
/// their home, or the one <c>THERMI_SENSORS_SOCKET</c> names in its place, as a test's session does. Anyone may serve a
/// socket at a path they can write while the service is stopped, so it is read only when the kernel says the person
/// serves it.
/// </summary>
internal sealed class MacSocketFeed(TimeProvider time, ILogger log) : SensorReader(time, log)
{
    protected override Task<(Stream Connection, bool Ours)> ConnectAsync(CancellationToken stop) =>
        ConnectAsync(SnapshotJson.MacSocketPath(), [User.Id], stop);
}
