using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using Thermi.Core.Updates;
using Thermi.SensorHost.Install;
using Thermi.SensorHost.Updates;

namespace Thermi.SensorHost.User;

/// <summary>
/// What a per-user install changes in Windows beyond its folder (docs/adr/0016-admin-on-demand.md): no service, no event
/// source and no driver, only the user's own host, which it stops, and the user's entry in Installed apps. The host's
/// "image path" is the version the state says runs, so an install by hand under a later one keeps the later, as the
/// service's does.
/// </summary>
internal sealed class UserSetup(InstallFolder folder, string pipe, SecurityIdentifier user) : ISystemSetup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "thermi";

    public string? ImagePath(string service)
    {
        var installed = service == SensorService.Name ? folder.Read().Installed : null;
        return installed is not null && File.Exists(folder.ServiceOf(installed)) ? $"\"{folder.ServiceOf(installed)}\"" : null;
    }

    public void Sc(params string[] arguments)
    {
        // No service: what the machine's install registers, a per-user one leaves out.
    }

    public void Stop(string service)
    {
        if (service == SensorService.Name)
        {
            new UserTarget(folder, pipe, user).Stop();
        }
    }

    public void Start(string service)
    {
        // The overlay starts the user's host from the launchers once the install lets go of the switch's lock.
    }

    public void Environment(string service, IReadOnlyList<string> variables)
    {
        // The host runs with the user's own environment, as the user.
    }

    public void EventSource(string service, bool registered)
    {
        // Only an administrator registers one: the user's host logs to a file.
    }

    public InstalledApp? App() => WindowsSetup.AppOf(Registry.CurrentUser);

    public void App(InstalledApp? app, string top) => WindowsSetup.WriteApp(Registry.CurrentUser, app, top);

    public void EndPrograms(string top) => new WindowsSetup().EndPrograms(top);

    // The value the window's "Start with Windows" reads and writes, quoted the same way.
    public void StartAtSignIn(string top, bool on)
    {
        var launcher = $"\"{Path.Combine(top, "thermi.exe")}\"";
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            run.SetValue(RunValue, launcher);
        }
        else if (run.GetValue(RunValue) is string value && string.Equals(value, launcher, StringComparison.OrdinalIgnoreCase))
        {
            run.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>The version the user's entry in Installed apps shows, once an update switched.</summary>
    public static void Show(ReleaseVersion version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(WindowsSetup.InstalledAppsKey, writable: true);
            key?.SetValue("DisplayVersion", version.ToString());
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // The switch is done either way; the entry shows the version before until the next.
            _ = ex;
        }
    }
}
