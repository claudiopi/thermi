using System.Diagnostics;
using Microsoft.Win32;
using Thermi.Core.Install;
using Thermi.Windows.Commands;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.ApplicationInstallationAndServicing;

namespace Thermi.Windows.Install;

/// <summary>
/// A per-user install's move to the machine's, once "install for everyone" put the service in
/// (docs/adr/0016-admin-on-demand.md): the per-user overlay points the start at sign-in at the machine's launcher and hands
/// over to it, and the machine's overlay then removes this user's per-user product once nothing runs from its folder.
/// </summary>
internal static class MachineMove
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\thermi-sensors";
    private const string ActiveSetupKey = @"Software\Microsoft\Active Setup\Installed Components\thermi";

    // Where the per-user MSI puts its install, which its product's entry does not name.
    private static readonly string PerUserTop =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "thermi");

    /// <summary>
    /// The machine's launcher, once the service serves from a machine install other than <paramref name="ownTop"/> and
    /// Windows Installer has finished it: the MSI starts the service before it writes its Active Setup, which the move marks.
    /// </summary>
    public static string? MachineLauncher(string ownTop)
    {
        // The registry first: a machine install, or a PC with no service, never asks the service manager each second.
        if (MachineTop() is not { } top || InstallLayout.Same(top, ownTop) || !Serves() || Installing())
        {
            return null;
        }

        var launcher = Path.Combine(top, "thermi.exe");
        return File.Exists(launcher) ? launcher : null;
    }

    public static bool IsMachines(string ownTop) => MachineTop() is { } top && InstallLayout.Same(top, ownTop);

    /// <summary>
    /// Points the user's start at sign-in at <paramref name="launcher"/> only when there is one, so one turned off stays off,
    /// and marks the machine's Active Setup done in this profile, so it never turns that start back on.
    /// </summary>
    public static void PointStartAt(string launcher)
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunAtSignIn.Key, writable: true))
        {
            if (run?.GetValue(RunAtSignIn.Name) is not null)
            {
                run.SetValue(RunAtSignIn.Name, RunAtSignIn.Of(launcher));
            }
        }

        using var machine = Registry.LocalMachine.OpenSubKey(ActiveSetupKey);
        if (machine?.GetValue("Version") is string version)
        {
            using var done = Registry.CurrentUser.CreateSubKey(ActiveSetupKey);
            done.SetValue("Version", version);
        }
    }

    /// <summary>This user's per-user thermi, by its UpgradeCode in the user's unmanaged context alone; null when there is none.</summary>
    public static string? PerUserProduct() => Related(InstallForEveryone.PerUserUpgradeCode).FirstOrDefault(UsersOwn);

    /// <summary>Whether a thermi program still runs from the per-user folder, which its removal would have to end.</summary>
    public static bool PerUserRuns()
    {
        var programs = InstallLayout.Programs.SelectMany(Process.GetProcessesByName).ToList();
        try
        {
            return programs.Any(RunsFromPerUser);
        }
        finally
        {
            programs.ForEach(program => program.Dispose());
        }
    }

    private static bool RunsFromPerUser(Process program)
    {
        try
        {
            return program.MainModule?.FileName is { } file && InstallLayout.Inside(file, PerUserTop);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Ended meanwhile, or not this user's to read: none of this user's per-user programs.
            return false;
        }
    }

    /// <summary>Removes the per-user product quietly, as the user, which needs no rights: Windows' own msiexec, by its path.</summary>
    public static void Remove(string product)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/x");
        start.ArgumentList.Add(product);
        start.ArgumentList.Add("/qn");
        Process.Start(start)?.Dispose();
    }

    // Windows Installer's own mutex, held while any install runs.
    private static bool Installing() => NamedMutex.Exists(@"Global\_MSIExecute");

    private static bool Serves() => new WindowsServices(StartSensors.Service).State() == ServiceRun.Running;

    // The folder above the version the service's image path runs from.
    private static string? MachineTop()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServiceKey);
        var exe = (key?.GetValue("ImagePath") as string)?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return Path.GetDirectoryName(Path.GetDirectoryName(exe));
    }

    // Every product of the UpgradeCode this user sees, a product code being 38 characters and its end.
    private static unsafe List<string> Related(string upgradeCode)
    {
        var found = new List<string>();
        var code = stackalloc char[39];
        fixed (char* upgrade = upgradeCode)
        {
            for (uint index = 0; PInvoke.MsiEnumRelatedProducts(upgrade, 0, index, code) == (uint)WIN32_ERROR.ERROR_SUCCESS; index++)
            {
                found.Add(new string(code));
            }
        }

        return found;
    }

    // Whether the product is installed for this user alone, unmanaged: the only context asked, so neither a machine's
    // install of it nor another user's is ever taken.
    private static unsafe bool UsersOwn(string code)
    {
        var version = stackalloc char[64];
        uint length = 64;
        fixed (char* product = code)
        fixed (char* property = "VersionString")
        {
            return PInvoke.MsiGetProductInfoEx(product, null, MSIINSTALLCONTEXT.MSIINSTALLCONTEXT_USERUNMANAGED, property, version, &length)
                == (uint)WIN32_ERROR.ERROR_SUCCESS;
        }
    }
}
