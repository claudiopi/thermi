using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Thermi.Cli.Agents;

namespace Thermi.Linux.Agents;

/// <summary>
/// Start at sign-in on Linux, for the window's switch and thermi-cli's <c>start-with-windows</c> alike: the overlay's user
/// unit, <c>thermi.service</c>, as the person's own <c>systemctl --user</c> enables, disables and reads it, so an enable of
/// systemd's, theirs or an administrator's for everyone, reads the same in both. The package enables it for no one
/// (docs/adr/0019-linux.md). A switch systemctl refused is an error, never one that only looks set.
/// </summary>
/// <param name="systemctl">The program asked, by its full path, never one found on the PATH.</param>
internal sealed class UserUnit(string systemctl = UserUnit.Systemctl) : IStartAtSignIn
{
    public const string Name = "thermi.service";

    public const string Systemctl = "/usr/bin/systemctl";

    /// <summary>The unit's file, where systemd would find it, or null when no package or test put one.</summary>
    public static string? File
    {
        get
        {
            var config = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            // In the order systemd looks: the user's settings, the system's, the user's own units, then the packages'.
            string[] folders = [Path.Combine(config, "systemd", "user"), "/etc/systemd/user", Path.Combine(data, "systemd", "user"),
                "/usr/local/lib/systemd/user", "/usr/lib/systemd/user"];
            return folders.Select(folder => Path.Combine(folder, Name)).FirstOrDefault(System.IO.File.Exists);
        }
    }

    public bool On => Run("is-enabled") == 0;

    public void Set(bool on)
    {
        var verb = on ? "enable" : "disable";
        if (Run(verb) is var exit and not 0)
        {
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"systemctl --user {verb} {Name} ended with {exit}."));
        }
    }

    private int Run(string verb)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(systemctl, ["--user", "--quiet", verb, Name]) { UseShellExecute = false });
            process?.WaitForExit();
            return process?.ExitCode ?? -1;
        }
        catch (Win32Exception)
        {
            return -1;
        }
    }
}
