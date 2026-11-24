using System.Security.Cryptography;
using Thermi.Core.Updates;
using Thermi.SensorHost.Updates;

namespace Thermi.SensorHost.Install;

/// <summary>
/// The layout an update keeps, laid down by the MSI and by <c>scripts/install.ps1</c> alike (docs/adr/0008-updates.md):
/// <c>thermi-sensors --adopt</c> copies its own version beside any other, points both services at it with the settings
/// docs/adr/0003-sensor-service.md measured, puts its launchers at the top and starts the service, which adopts it.
/// <c>--remove</c> undoes all of it. Both run as SYSTEM or an administrator.
/// </summary>
internal sealed class Installation(InstallFolder folder, ISystemSetup system, Action<string> say)
{
    public const string ElevatedRights = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLORC;;;IU)(A;;CCLCSWLORC;;;SU)";

    /// <summary>The one privilege the service keeps of LocalSystem's: the sensors read the same with it alone.</summary>
    public const string Privileges = "SeChangeNotifyPrivilege";

    /// <summary>A 2 MB young generation, in hex, which keeps the service in docs/adr/0011-sensor-service-memory-budget.md's budget.</summary>
    public const string YoungGeneration = "DOTNET_GCgen0size=200000";

    private const string Sensors = SensorService.Name;
    private const string Elevated = ServiceConfig.Elevated;

    /// <summary>
    /// Lays <paramref name="version"/> down from <paramref name="from"/>, where it was installed; 0 when it runs, 1 when
    /// Windows refused a step, which leaves the services where they were.
    /// </summary>
    public int Adopt(ReleaseVersion version, string from, string? product)
    {
        if (!folder.Trusted())
        {
            say($"{folder.Top} is not a folder only administrators change, so thermi is not installed there.");
            return 1;
        }

        var before = (Sensors: system.ImagePath(Sensors), Elevated: system.ImagePath(Elevated));
        if (Running(before.Sensors) is { } running && running > version && Directory.Exists(folder.VersionOf(running)))
        {
            // An update already runs a later version: an MSI of this one, repaired or upgraded to, does not take it back.
            system.InstalledApp(product, running, folder.Top);
            return 0;
        }

        system.Stop(Sensors);
        if (!Copy(from, folder.VersionOf(version)))
        {
            say($"{folder.VersionOf(version)} holds other files and is in use: close thermi and install again.");
            PutBack(before.Sensors, before.Elevated);
            return 1;
        }

        try
        {
            Register(folder.ServiceOf(version), before.Sensors is not null, before.Elevated is not null);
        }
        catch (SetupRefusedException refused)
        {
            say($"Windows refused to register thermi's services, so the thermi installed before stays: {refused.Message}");
            PutBack(before.Sensors, before.Elevated);
            return 1;
        }

        folder.PlaceLaunchers(version);
        Tidy(from);
        system.InstalledApp(product, version, folder.Top);
        system.Start(Sensors);
        return 0;
    }

    /// <summary>
    /// Removes both services, their log source, Installed apps' entry, and every version and launcher but the folder it
    /// runs from, <paramref name="own"/>, which the MSI removes, or scripts/install.ps1 once it ended.
    /// </summary>
    public void Remove(ReleaseVersion version, string own)
    {
        system.Stop(Sensors);
        foreach (var service in new[] { Elevated, Sensors })
        {
            if (system.ImagePath(service) is not null)
            {
                system.Sc("delete", service);
            }
        }

        system.EventSource(Sensors, registered: false);
        system.InstalledApp(null, version, folder.Top);
        system.EndOverlays(folder.Top);
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Top).Where(entry => !SamePath(entry, own)))
        {
            Delete(entry);
        }
    }

    /// <summary>The folder the MSI installs a version in, beside the ones the service keeps.</summary>
    public const string Setup = "setup";

    // The version of a service run from a version's folder; null for one run from anywhere else, as a build before
    // these folders ran.
    private ReleaseVersion? Running(string? imagePath)
    {
        var exe = imagePath?.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var parent = exe is null ? null : Path.GetDirectoryName(exe);
        return parent is not null && SamePath(Path.GetDirectoryName(parent) ?? "", folder.Top) ? ReleaseVersion.Parse(Path.GetFileName(parent)) : null;
    }

    // What a build from before the version folders left at the top, which nothing runs once the launchers are there.
    private void Tidy(string from)
    {
        var launchers = Directory.EnumerateFiles(folder.Top, "*.exe").Where(exe => File.Exists(Path.Combine(from, "launchers", Path.GetFileName(exe))));
        var known = new HashSet<string>(launchers, StringComparer.OrdinalIgnoreCase) { folder.Update, Path.Combine(folder.Top, Setup) };
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Top))
        {
            var kept = known.Contains(entry) || SamePath(entry, from) || (Directory.Exists(entry) && ReleaseVersion.Parse(Path.GetFileName(entry)) is not null);
            if (!kept)
            {
                folder.SetAside(entry);
            }
        }
    }

    private static bool SamePath(string one, string other) => string.Equals(Full(one), Full(other), StringComparison.OrdinalIgnoreCase);

    private static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    // A version already there with the same files stays, as an update unpacked it; one built again with other files is
    // replaced, which Windows refuses while anything runs from it. False then.
    private bool Copy(string from, string to)
    {
        if (SamePath(from, to) || Same(from, to))
        {
            return true;
        }

        if (Directory.Exists(to) && !folder.SetAside(to))
        {
            return false;
        }

        var fresh = folder.Fresh("unpack-") ?? throw new IOException($"{folder.Update} is not a folder only administrators change.");
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(fresh, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        Directory.Move(fresh, to);
        return true;
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

    // A failure to put back is said, not thrown: what failed first is what the install tells. The service starts again
    // either way, from wherever it points.
    private void PutBack(string? sensors, string? elevated)
    {
        foreach (var (service, image) in new[] { (Elevated, elevated), (Sensors, sensors) })
        {
            try
            {
                if (image is not null)
                {
                    system.Sc("config", service, "binPath=", image);
                }
            }
            catch (SetupRefusedException again)
            {
                say($"The thermi installed before could not be put back: {again.Message}");
            }
        }

        if (sensors is not null)
        {
            system.Start(Sensors);
        }
    }

    // A file an overlay that would not end still holds is left behind, and only it.
    private static void Delete(string entry)
    {
        try
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
        }
    }
}
