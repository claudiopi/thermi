using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Overlay;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The window's sensors as a test sets them, instead of the pipe: the service's state, running unless told, and whatever
/// snapshot it is given.
/// </summary>
internal sealed class TestFeed : ISensorFeed
{
    public event EventHandler? Received;

    public Snapshot? Latest { get; set; }

    public ServiceState State { get; set; } = ServiceState.Running;

    public Task RunAsync(CancellationToken stop)
    {
        Received?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
