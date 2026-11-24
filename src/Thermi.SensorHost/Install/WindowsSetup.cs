using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Install;

/// <summary>The install's changes on this PC, as SYSTEM or an administrator: the service manager through Windows' own <c>sc.exe</c>.</summary>
internal sealed class WindowsSetup : ISystemSetup
{
    /// <summary>Installed apps' key for thermi, which the MSI hides its own for, so the version it shows is the one that runs.</summary>
    public const string InstalledAppsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\thermi";

    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";

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
        if (ImagePath(service) is null)
        {
            return;
        }

        using var controller = new ServiceController(service);
        if (controller.Status != ServiceControllerStatus.Stopped)
        {
            controller.Stop();
            controller.WaitForStatus(ServiceControllerStatus.Stopped, Wait);
        }
    }

    public void Start(string service)
    {
        using var controller = new ServiceController(service);
        controller.Start();
    }

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

    public void InstalledApp(string? product, ReleaseVersion version, string top)
    {
        if (product is null)
        {
            Registry.LocalMachine.DeleteSubKeyTree(InstalledAppsKey, throwOnMissingSubKey: false);
            return;
        }

        using var key = Registry.LocalMachine.CreateSubKey(InstalledAppsKey);
        key.SetValue("DisplayName", "thermi");
        key.SetValue("DisplayVersion", version.ToString());
        key.SetValue("Publisher", "thermi");
        key.SetValue("DisplayIcon", Path.Combine(top, "thermi.exe"));
        key.SetValue("InstallLocation", top);
        key.SetValue("UninstallString", $"MsiExec.exe /X{product}");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>The version Installed apps shows, once an update switched; nothing when no MSI installed thermi.</summary>
    public static void Show(ReleaseVersion version)
    {
        using var key = Registry.LocalMachine.OpenSubKey(InstalledAppsKey, writable: true);
        key?.SetValue("DisplayVersion", version.ToString());
    }

    public void EndOverlays(string top)
    {
        foreach (var overlay in Process.GetProcessesByName("thermi"))
        {
            using (overlay)
            {
                try
                {
                    if (overlay.MainModule?.FileName.StartsWith(top + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        overlay.Kill();
                        overlay.WaitForExit(Wait);
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
