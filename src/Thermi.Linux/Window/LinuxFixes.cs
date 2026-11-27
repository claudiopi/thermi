using System.Diagnostics;

namespace Thermi.Linux.Window;

/// <summary>
/// What the window starts on Linux: the overlay, as the person's user unit, and the stopped sensor service, whose socket
/// polkit's prompt starts. thermi itself never runs as root; GNOME's extension is the Shell's, through <see cref="Gnome.ShellLink"/>.
/// </summary>
internal static class LinuxFixes
{
    public const string OverlayUnit = "thermi.service";

    private const string Systemctl = SignInUnit.Systemctl;
    private const string Pkexec = "/usr/bin/pkexec";

    /// <summary>Starts the overlay's user unit, as the person: false when systemctl failed.</summary>
    public static Task<bool> StartOverlay() => RunAsync(Systemctl, ["--user", "start", OverlayUnit]);

    /// <summary>Starts the sensor service's socket behind polkit's prompt: false when the person declined or it failed.</summary>
    public static Task<bool> StartService() => RunAsync(Pkexec, [Systemctl, "start", "thermi-sensors.socket"]);

    // By its full path, never a program found on the PATH; one that is not there fails.
    private static async Task<bool> RunAsync(string program, IReadOnlyList<string> args)
    {
        try
        {
            using var process = Process.Start(Info(program, args));
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static ProcessStartInfo Info(string program, IReadOnlyList<string> args) => new(program, args)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
}
