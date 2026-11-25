using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using Thermi.Core.Install;
using Thermi.Core.Updates;
using Thermi.SensorHost.Updates;

namespace Thermi.SensorHost.Install;

/// <summary>The install's changes on this PC, as SYSTEM or an administrator: the service manager through Windows' own <c>sc.exe</c>.</summary>
internal sealed class WindowsSetup : ISystemSetup
{
    /// <summary>Installed apps' key for thermi, which the MSI hides its own for, so the version it shows is the one that runs.</summary>
    public const string InstalledAppsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\thermi";

    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";
    private const string Uninstall = "MsiExec.exe /X";

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    public string? ImagePath(string service)
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + service);
        return key?.GetValue("ImagePath") as string;
    }

    public void Sc(params string[] arguments)
    {
        // Windows' own, by its full path: nothing in the PATH a user set is run as SYSTEM.
        var start = new ProcessStartInfo(Path.Combine(System.Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var sc = Process.Start(start) ?? throw new SetupRefusedException("sc.exe did not start.");
        var said = sc.StandardOutput.ReadToEnd();
        sc.WaitForExit();
        if (sc.ExitCode != 0)
        {
            throw new SetupRefusedException($"sc.exe {arguments[0]} {arguments[1]} answered {sc.ExitCode}: {said.Trim()}");
        }
    }

    public void Stop(string service)
    {
        if (ImagePath(service) is not null)
        {
            ServiceConfig.Stop(service);
        }
    }

    public void Start(string service) => ServiceConfig.Start(service);

    public void Environment(string service, IReadOnlyList<string> variables)
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServicesKey + service, writable: true)
            ?? throw new SetupRefusedException($"The {service} service has no key.");
        key.SetValue("Environment", variables.ToArray(), RegistryValueKind.MultiString);
    }

    public void EventSource(string service, bool registered)
    {
        if (registered && !EventLog.SourceExists(service))
        {
            EventLog.CreateEventSource(service, "Application");
        }
        else if (!registered && EventLog.SourceExists(service))
        {
            EventLog.DeleteEventSource(service);
        }
    }

    public InstalledApp? App() => AppOf(Registry.LocalMachine);

    public void App(InstalledApp? app, string top) => WriteApp(Registry.LocalMachine, app, top);

    /// <summary>thermi's entry in Installed apps under <paramref name="hive"/>: the machine's, or a per-user install's own.</summary>
    public static InstalledApp? AppOf(RegistryKey hive)
    {
        using var key = hive.OpenSubKey(InstalledAppsKey);
        return key?.GetValue("UninstallString") is string uninstall && uninstall.StartsWith(Uninstall, StringComparison.OrdinalIgnoreCase)
            ? new InstalledApp(uninstall[Uninstall.Length..], key.GetValue("DisplayVersion") as string ?? "")
            : null;
    }

    /// <summary>Writes thermi's entry in Installed apps under <paramref name="hive"/>, or with null removes it.</summary>
    public static void WriteApp(RegistryKey hive, InstalledApp? app, string top)
    {
        if (app is null)
        {
            hive.DeleteSubKeyTree(InstalledAppsKey, throwOnMissingSubKey: false);
            return;
        }

        using var key = hive.CreateSubKey(InstalledAppsKey);
        key.SetValue("DisplayName", "thermi");
        key.SetValue("DisplayVersion", app.Version);
        key.SetValue("Publisher", "thermi");
        key.SetValue("DisplayIcon", Path.Combine(top, "thermi.exe"));
        key.SetValue("InstallLocation", top);
        key.SetValue("UninstallString", Uninstall + app.Product);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// The version Installed apps shows, once an update switched; nothing when no MSI installed thermi. A key Windows
    /// would not let be written leaves the version it showed: the switch is done either way.
    /// </summary>
    public static void Show(ReleaseVersion version)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(InstalledAppsKey, writable: true);
            key?.SetValue("DisplayVersion", version.ToString());
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            _ = ex;
        }
    }

    public void StartAtSignIn(string top, bool on)
    {
        // The machine's MSI turns it on in each profile through Active Setup, and each person may turn it off for good.
    }

    public void EndPrograms(string top)
    {
        foreach (var program in Process.GetProcessesByName("thermi").Concat(Process.GetProcessesByName("thermi-sensors")))
        {
            using (program)
            {
                try
                {
                    if (program.Id != System.Environment.ProcessId
                        && program.MainModule?.FileName is { } file && InstallLayout.Inside(file, top))
                    {
                        program.Kill();
                        program.WaitForExit(Wait);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // One that ended meanwhile, or that Windows would not let be read: the folder's delete tells.
                    _ = ex;
                }
            }
        }
    }
}
