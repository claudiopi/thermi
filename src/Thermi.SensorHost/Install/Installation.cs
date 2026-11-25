using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Cryptography;
using Thermi.Core.Updates;
using Thermi.SensorHost.Updates;

namespace Thermi.SensorHost.Install;

/// <summary>
/// The layout an update keeps, laid down by the MSI and by <c>scripts/install.ps1</c> alike (docs/adr/0008-updates.md):
/// <c>thermi-sensors --adopt</c> copies its own version beside any other, points both services at it with the settings
/// docs/adr/0003-sensor-service.md measured, puts its launchers at the top and starts the service. <c>--undo</c> puts back
/// what the last one changed, for the MSI's rollback, and <c>--remove</c> takes thermi off. Each runs as SYSTEM or an
/// administrator, from a folder of thermi's own, and holds the switch's lock, so no update switches meanwhile.
/// </summary>
internal sealed class Installation(InstallFolder folder, ISystemSetup system, TimeProvider time, Action<string> say)
{
    public const string ElevatedRights = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLORC;;;IU)(A;;CCLCSWLORC;;;SU)";

    /// <summary>The one privilege the service keeps of LocalSystem's: the sensors read the same with it alone.</summary>
    public const string Privileges = "SeChangeNotifyPrivilege";

    /// <summary>A 2 MB young generation, in hex, which keeps the service in docs/adr/0011-sensor-service-memory-budget.md's budget.</summary>
    public const string YoungGeneration = "DOTNET_GCgen0size=200000";

    /// <summary>The folder the MSI installs a version in, beside the ones the service keeps, and which only the MSI removes.</summary>
    public const string Setup = "setup";

    /// <summary>The start of the folder scripts/install.ps1 copies a build to before it adopts it.</summary>
    public const string InstallPrefix = "install-";

    private const string Sensors = SensorService.Name;
    private const string Elevated = ServiceConfig.Elevated;

    // A watchdog holds the lock while it gives a version its 120 polls, about four minutes for one that never serves and
    // twelve for one that connects and never writes; an install waits fifteen, 200 ms a try.
    private const int LockTries = 4500;

    private const string Switching = "An update is switching thermi's version, so nothing was changed: try again in a few minutes.";

    private static readonly string[] Launchers = ["thermi.exe", "thermi-sensors.exe"];

    private string UndoPath => Path.Combine(folder.Update, "adopt-undo.json");

    /// <summary>Whether <paramref name="own"/> is a folder an install runs from: the MSI's, the script's or a version's.</summary>
    public static bool InstallsFrom(string own)
    {
        var name = Path.GetFileName(Full(own));
        return string.Equals(name, Setup, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(InstallPrefix, StringComparison.OrdinalIgnoreCase)
            || ReleaseVersion.Parse(name) is not null;
    }

    /// <summary>
    /// Lays <paramref name="version"/> down from <paramref name="from"/>; 0 when it runs, 1 when a step failed, which puts
    /// back what was there. An install while an update runs a later version leaves the later one.
    /// </summary>
    public async Task<int> AdoptAsync(ReleaseVersion version, string from, string? product, CancellationToken stop)
    {
        if (Refuses(from))
        {
            return 1;
        }

        // What an install before this one recorded is no longer what to put back, whatever this one does.
        DeleteUndo();

        using var held = await new SwitchLock(folder, time).HoldAsync(LockTries, stop);
        if (held is null)
        {
            say(Switching);
            return 1;
        }

        var there = Directory.Exists(folder.VersionOf(version));
        var undo = new AdoptUndo(system.ImagePath(Sensors), system.ImagePath(Elevated), there, folder.StateText(), system.App());
        await WriteUndoAsync(undo, stop);
        var shownBy = product ?? undo.App?.Product;
        if (Running(undo.Sensors) is { } running && running > version && Directory.Exists(folder.VersionOf(running)))
        {
            Show(shownBy, running);
            return 0;
        }

        return Adopt(version, from, shownBy, undo);
    }

    /// <summary>Puts back what the last install changed, as its file in <c>update\</c> recorded; nothing when there is none.</summary>
    public async Task<int> UndoAsync(ReleaseVersion version, string from, CancellationToken stop)
    {
        if (Refuses(from))
        {
            return 1;
        }

        using var held = await new SwitchLock(folder, time).HoldAsync(LockTries, stop);
        if (held is null)
        {
            say(Switching);
            return 1;
        }

        if (AdoptUndo.Read(File.Exists(UndoPath) ? await File.ReadAllTextAsync(UndoPath, stop) : null) is { } undo)
        {
            Restore(undo, version);
            DeleteUndo();
        }

        return 0;
    }

    /// <summary>Drops what the last install recorded to put back, once the MSI committed it.</summary>
    public int Forget(string from)
    {
        if (Refuses(from))
        {
            return 1;
        }

        DeleteUndo();
        return 0;
    }

    /// <summary>
    /// Removes both services, their log source, Installed apps' entry, every version, the launchers and <c>update\</c>,
    /// all but the folder it runs from, <paramref name="own"/>, and the MSI's, which the MSI removes, or scripts/install.ps1.
    /// </summary>
    public async Task<int> RemoveAsync(string own, CancellationToken stop)
    {
        if (Refuses(own))
        {
            return 1;
        }

        // The lock lives in update\, so it is let go before update\ goes.
        using (var held = await new SwitchLock(folder, time).HoldAsync(LockTries, stop))
        {
            if (held is null)
            {
                say(Switching);
                return 1;
            }

            try
            {
                TakeOff();
            }
            catch (Exception ex) when (Failed(ex))
            {
                say($"thermi could not be removed: {ex.Message}");
                return 1;
            }
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Top).Where(entry => Owned(entry) && !SamePath(entry, own)))
        {
            InstallFolder.Delete(entry);
        }

        return 0;
    }

    // No user may open it, so none holds it against the next install's delete.
    private async Task WriteUndoAsync(AdoptUndo undo, CancellationToken stop)
    {
        await using var file = new FileInfo(UndoPath)
            .Create(FileMode.Create, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, folder.Private());
        await using var writer = new StreamWriter(file);
        await writer.WriteAsync(undo.ToJson().AsMemory(), stop);
    }

    private void DeleteUndo()
    {
        if (File.Exists(UndoPath))
        {
            File.Delete(UndoPath);
        }
    }

    private void TakeOff()
    {
        foreach (var service in new[] { Elevated, Sensors })
        {
            system.Stop(service);
            if (system.ImagePath(service) is not null)
            {
                system.Sc("delete", service);
            }
        }

        system.EventSource(Sensors, registered: false);
        system.App(null, folder.Top);
        system.StartAtSignIn(folder.Top, on: false);
        system.EndPrograms(folder.Top);
    }

    private int Adopt(ReleaseVersion version, string from, string? product, AdoptUndo undo)
    {
        try
        {
            system.Stop(Sensors);
            Copy(from, version);
            Register(folder.ServiceOf(version), undo.Sensors is not null, undo.Elevated is not null);
            folder.PlaceLaunchers(version);
            if (undo.Sensors is null)
            {
                system.StartAtSignIn(folder.Top, on: true);
            }

            folder.Write(SwitchHealth.InstalledByHand(folder.Read() with { Switch = null }, version));
            Show(product, version);
            Tidy(from);
            system.Start(Sensors);
            return 0;
        }
        catch (Exception ex) when (Failed(ex))
        {
            say($"thermi {version} could not be installed, so the thermi installed before stays: {ex.Message}");
            Restore(undo, version);
            return 1;
        }
    }

    private static bool Failed(Exception ex) => ex is SetupRefusedException or IOException or UnauthorizedAccessException or InvalidOperationException
        or Win32Exception or System.ServiceProcess.TimeoutException;

    // What failed first is what the install tells, so what fails here is only said.
    private void Restore(AdoptUndo undo, ReleaseVersion version)
    {
        Try(() => system.Stop(Sensors));
        foreach (var (service, image) in new[] { (Elevated, undo.Elevated), (Sensors, undo.Sensors) })
        {
            Try(() =>
            {
                if (image is not null)
                {
                    system.Sc("config", service, "binPath=", image);
                }
                else if (system.ImagePath(service) is not null)
                {
                    system.Sc("delete", service);
                }
            });
        }

        if (undo.Sensors is null)
        {
            Try(() => system.EventSource(Sensors, registered: false));
            Try(() => system.StartAtSignIn(folder.Top, on: false));
        }

        if (!undo.VersionThere)
        {
            Try(() => folder.SetAside(folder.VersionOf(version)));
        }

        Try(() => PutLaunchersBack(undo));
        Try(() => folder.RestoreState(undo.State));
        Try(() => system.App(undo.App, folder.Top));
        if (undo.Sensors is not null)
        {
            Try(() => system.Start(Sensors));
        }
    }

    private void PutLaunchersBack(AdoptUndo undo)
    {
        if (Running(undo.Sensors) is { } running && Directory.Exists(folder.VersionOf(running)))
        {
            folder.PlaceLaunchers(running);
        }
        else if (undo.Sensors is null)
        {
            foreach (var launcher in Launchers)
            {
                folder.SetAside(Path.Combine(folder.Top, launcher));
            }
        }
    }

    private void Try(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (Failed(ex))
        {
            say($"The thermi installed before could not be put back whole: {ex.Message}");
        }
    }

    private void Show(string? product, ReleaseVersion version)
    {
        if (product is not null)
        {
            system.App(new InstalledApp(product, version.ToString()), folder.Top);
        }
    }

    // Only from a folder of thermi's, in a folder only administrators change: its parent is what it removes from.
    private bool Refuses(string own)
    {
        if (InstallsFrom(own) && SamePath(Path.GetDirectoryName(Full(own)) ?? "", folder.Top) && folder.Trusted())
        {
            return false;
        }

        say($"{own} is not a folder of thermi's in a folder only administrators change, so nothing was changed.");
        return true;
    }

    private bool Owned(string entry)
    {
        var name = Path.GetFileName(entry);
        return Directory.Exists(entry)
            ? SamePath(entry, folder.Update) || name.StartsWith(InstallPrefix, StringComparison.OrdinalIgnoreCase) || ReleaseVersion.Parse(name) is not null
            : Launchers.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    // A version already there with the same files stays, as an update unpacked it; one built again with other files is
    // replaced, which Windows refuses while anything runs from it.
    private void Copy(string from, ReleaseVersion version)
    {
        var to = folder.VersionOf(version);
        if (SamePath(from, to) || Same(from, to))
        {
            return;
        }

        var fresh = folder.Fresh("unpack-") ?? throw new IOException($"{folder.Update} is not a folder only administrators change.");
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(fresh, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        if (!folder.Place(fresh, version))
        {
            folder.SetAside(fresh);
            throw new SetupRefusedException($"{to} holds other files and is in use: close thermi and install again.");
        }
    }

    private static bool Same(string one, string other) => Directory.Exists(other) && Tree(one).SequenceEqual(Tree(other));

    private static IEnumerable<string> Tree(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Select(file => $"{Path.GetRelativePath(folder, file)} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}")
        .Order(StringComparer.OrdinalIgnoreCase);

    private void Register(string exe, bool sensorsThere, bool elevatedThere)
    {
        var path = $"\"{exe}\"";
        system.Sc(sensorsThere ? ["config", Sensors, "binPath=", path, "start=", "auto"]
            : ["create", Sensors, "binPath=", path, "start=", "auto", "DisplayName=", "thermi sensors"]);
        system.Sc("description", Sensors, "Reads hardware sensors for the thermi overlay.");
        system.Sc("failure", Sensors, "reset=", "86400", "actions=", "restart/5000/restart/5000/restart/60000");

        // A service that stops itself with an error code counts as failed too, so it gets restarted.
        system.Sc("failureflag", Sensors, "1");
        system.Sc("privs", Sensors, Privileges);
        system.Environment(Sensors, [YoungGeneration]);
        system.EventSource(Sensors, registered: true);

        var fixes = $"{path} --elevated";
        system.Sc(elevatedThere ? ["config", Elevated, "binPath=", fixes, "start=", "demand"]
            : ["create", Elevated, "binPath=", fixes, "start=", "demand", "DisplayName=", "thermi fixes"]);
        system.Sc("description", Elevated, "Installs the sensor driver or starts thermi's sensors, when an administrator asks from thermi.");
        system.Sc("sdset", Elevated, ElevatedRights);
    }

    // The version of a service run from a version's folder; null for one run from anywhere else, as a build before
    // these folders ran.
    private ReleaseVersion? Running(string? imagePath)
    {
        var exe = imagePath?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var parent = exe is null ? null : Path.GetDirectoryName(exe);
        return parent is not null && SamePath(Path.GetDirectoryName(parent) ?? "", folder.Top) ? ReleaseVersion.Parse(Path.GetFileName(parent)) : null;
    }

    // What a build from before the version folders left at the top: only the names this version ships, launchers aside.
    private void Tidy(string from)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Top))
        {
            var name = Path.GetFileName(entry);
            var shipped = File.Exists(Path.Combine(from, name)) || Directory.Exists(Path.Combine(from, name));
            if (shipped && !Launchers.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                folder.SetAside(entry);
            }
        }
    }

    private static bool SamePath(string one, string other) => string.Equals(Full(one), Full(other), StringComparison.OrdinalIgnoreCase);

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
