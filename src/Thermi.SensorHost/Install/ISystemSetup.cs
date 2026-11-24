using Thermi.Core.Updates;

namespace Thermi.SensorHost.Install;

/// <summary>What an install changes in Windows beyond thermi's folder: the services, their log source, Installed apps and the overlays that run.</summary>
internal interface ISystemSetup
{
    /// <summary>The service's image path, or null when there is no such service.</summary>
    string? ImagePath(string service);

    /// <summary>Runs the service manager's <c>sc.exe</c>; throws <see cref="SetupRefusedException"/> when it refuses.</summary>
    void Sc(params string[] arguments);

    void Stop(string service);

    void Start(string service);

    /// <summary>The variables the service alone runs with, in its own key, so deleting the service removes them.</summary>
    void Environment(string service, IReadOnlyList<string> variables);

    void EventSource(string service, bool registered);

    /// <summary>thermi's entry in Installed apps, removable through the MSI of <paramref name="product"/>; null removes it.</summary>
    void InstalledApp(string? product, ReleaseVersion version, string top);

    /// <summary>Ends every overlay that runs from <paramref name="top"/>, in every session.</summary>
    void EndOverlays(string top);
}

