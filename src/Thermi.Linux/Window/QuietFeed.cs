using Thermi.Core.Pipe;
using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.Linux.Window;

/// <summary>
/// A sensor feed that reads nothing, as a service that is not running: the window shows its fix. It stands where the
/// service's socket feed goes, which reads <c>thermi-sensors</c>' snapshots.
/// </summary>
internal sealed class QuietFeed : ISensorFeed
{
    public event EventHandler? Received
    {
        add
        {
            // None ever comes.
        }

        remove
        {
            // None ever came.
        }
    }

    public Snapshot? Latest => null;

    public ServiceState State => ServiceState.Stopped;

    public Task RunAsync(CancellationToken stop) => Task.CompletedTask;
}
