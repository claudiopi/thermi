namespace Thermi.SensorHost.Install;

/// <summary>What an install changes in Windows beyond thermi's folder: the services, their log source, Installed apps and the programs that run.</summary>
internal interface ISystemSetup
{
    /// <summary>The service's image path, or null when there is no such service.</summary>
    string? ImagePath(string service);

    /// <summary>Runs the service manager's <c>sc.exe</c>; throws <see cref="SetupRefusedException"/> when it refuses.</summary>
    void Sc(params string[] arguments);

    /// <summary>Stops the service and waits for it, when there is one.</summary>
    void Stop(string service);

    void Start(string service);

    /// <summary>The variables the service alone runs with, in its own key, so deleting the service removes them.</summary>
    void Environment(string service, IReadOnlyList<string> variables);

    void EventSource(string service, bool registered);

    /// <summary>thermi's entry in Installed apps: the MSI it uninstalls through and the version it shows, or null.</summary>
    InstalledApp? App();

    /// <summary>Writes thermi's entry in Installed apps, or with null removes it.</summary>
    void App(InstalledApp? app, string top);

    /// <summary>
    /// Turns on the start at sign-in of the launcher in <paramref name="top"/>, on a first install; with false, takes off
    /// that start only while it still names this launcher.
    /// </summary>
    void StartAtSignIn(string top, bool on);

    /// <summary>Ends every thermi program that runs from <paramref name="top"/>, in every session, but this process.</summary>
    void EndPrograms(string top);
}

/// <summary>thermi's entry in Installed apps: the MSI's product code, which uninstalls it, and the version shown.</summary>
internal sealed record InstalledApp(string Product, string Version);
