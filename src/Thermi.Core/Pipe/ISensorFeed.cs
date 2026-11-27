using Thermi.Core.Sensors;
using Thermi.Core.Tray;

namespace Thermi.Core.Pipe;

/// <summary>Where a host's snapshots come from: the sensor host's pipe, as the overlay reads it, or a recording in its tests.</summary>
public interface ISensorFeed
{
    /// <summary>Raised on the reader's thread for every snapshot that comes, once it is <see cref="Latest"/>.</summary>
    event EventHandler? Received;

    /// <summary>Null while the sensor host is unreachable or has gone quiet.</summary>
    Snapshot? Latest { get; }

    /// <summary>What the overlay shows: <see cref="Latest"/>, or while the service sends none, what the host adds itself.</summary>
    Snapshot Shown => Latest ?? new Snapshot([]);

    /// <summary>Whether the readings are fresh or awaited, the service is not running, or its data went stale.</summary>
    ServiceState State { get; }

    /// <summary>Whether, the service stopped, a program other than it holds a pipe of its name, which the service then waits out.</summary>
    bool Taken => false;

    Task RunAsync(CancellationToken stop);
}
