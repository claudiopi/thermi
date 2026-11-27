using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Thermi.Cli.Agents;

namespace Thermi.Linux.Window;

/// <summary>
/// Start at sign-in on Linux, for the window's switch and thermi-cli's <c>start-with-windows</c> alike: the overlay's user
/// unit, which each person enables or disables with <c>systemctl --user</c>; the package enables it for no one
/// (docs/adr/0019-linux.md). A switch systemctl refused is an error, never one that only looks set.
/// </summary>
/// <param name="systemctl">The program asked, by its full path, never one found on the PATH.</param>
internal sealed class SignInUnit(string systemctl = SignInUnit.Systemctl) : IStartAtSignIn
{
    public const string Systemctl = "/usr/bin/systemctl";

    public bool On => Run("is-enabled") == 0;

    public void Set(bool on)
    {
        var verb = on ? "enable" : "disable";
        if (Run(verb) is var exit and not 0)
        {
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"systemctl --user {verb} {LinuxFixes.OverlayUnit} ended with {exit}."));
        }
    }

    private int Run(string verb)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(systemctl, ["--user", "--quiet", verb, LinuxFixes.OverlayUnit]) { UseShellExecute = false });
            process?.WaitForExit();
            return process?.ExitCode ?? -1;
        }
        catch (Win32Exception)
        {
            return -1;
        }
    }
}
