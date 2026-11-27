using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Agents;

/// <summary>
/// Start at sign-in on Linux, for the window's switch and thermi-cli's <c>start-with-windows</c> alike: the overlay's user
/// unit, <c>thermi.service</c>, as the person's own <c>systemctl --user</c> enables, disables and reads it, so an enable of
/// systemd's, theirs or an administrator's for everyone, reads the same in both. The package enables it for no one
/// (docs/adr/0019-linux.md); thermi's first start in each person's session does, once (<see cref="EnableOnce"/>). A switch
/// systemctl refused is an error, never one that only looks set.
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

    /// <summary>Where <see cref="EnableOnce"/> marks that it turned start at sign-in on: beside the settings.</summary>
    public static string Marker { get; } = Path.Combine(Path.GetDirectoryName(ConfigStore.DefaultPath)!, "start-at-sign-in");

    public bool On => Run("is-enabled") == 0;

    /// <summary>
    /// Turns start at sign-in on at thermi's first start in a person's session, as Windows' installer does: once, so a
    /// person who turned it off finds it off at every start after. <paramref name="marker"/> says it was done; a user
    /// manager that would not enable it leaves none, and the next start tries again.
    /// </summary>
    public void EnableOnce(string marker, ILogger log)
    {
        if (System.IO.File.Exists(marker))
        {
            return;
        }

        try
        {
            Set(on: true);
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            System.IO.File.WriteAllBytes(marker, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.SignInNotEnabled(ex);
        }
    }

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
