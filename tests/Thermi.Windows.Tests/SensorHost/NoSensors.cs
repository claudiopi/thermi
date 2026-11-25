extern alias sensors;

using sensors::Thermi.SensorHost;
using Thermi.Core.Sensors;

namespace Thermi.Windows.Tests.SensorHost;

/// <summary>Sensors that read none, for tests of what the service adds to them.</summary>
internal sealed class NoSensors : ISensorSource
{
    public Task<Snapshot> ReadAsync(CancellationToken stop) => Task.FromResult(new Snapshot([]));

    public void Dispose()
    {
    }
}
