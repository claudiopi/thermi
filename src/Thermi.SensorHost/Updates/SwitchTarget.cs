using System.Security.Principal;
using Thermi.Core.Updates;
using Thermi.Trust;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// What a switch stops, points and starts: the services of a machine install (docs/adr/0008-updates.md), or the user's own
/// host in a per-user one (docs/adr/0016-admin-on-demand.md).
/// </summary>
internal interface ISwitchTarget
{
    /// <summary>What the watchdog is told besides <c>--switch &lt;version&gt;</c>, so it switches the same target.</summary>
    string? Argument { get; }

    void Stop();

    /// <summary>Makes <paramref name="version"/> the one Windows starts after a cut; a host started by hand has nothing to point.</summary>
    void Point(ReleaseVersion version);

    /// <summary>Starts <paramref name="version"/>, as pointed for the services, or its own host.</summary>
    void Start(ReleaseVersion version);

    bool Owns(SecurityIdentifier owner);
}

/// <summary>The services of a machine install: pointed at a version, then started and stopped by the service manager.</summary>
internal sealed class ServiceTarget(InstallFolder folder) : ISwitchTarget
{
    private readonly ServiceConfig _services = new(folder);

    public string? Argument => null;

    public void Stop() => ServiceConfig.Stop();

    public void Point(ReleaseVersion version) => _services.Point(version);

    public void Start(ReleaseVersion version) => ServiceConfig.Start();

    public bool Owns(SecurityIdentifier owner) => AdminFolder.IsAdministrator(owner);
}
