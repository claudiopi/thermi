using System.Diagnostics;
using Thermi.Linux.Agents;

namespace Thermi.Linux.Window;

/// <summary>
/// What the window starts on Linux: the overlay, as the person's user unit, and the stopped sensor service, whose socket
/// polkit's prompt starts. thermi itself never runs as root; GNOME's extension is the Shell's, through <see cref="Gnome.ShellLink"/>.
/// </summary>
internal static class LinuxFixes
{
    private const string SystemdRun = "/usr/bin/systemd-run";
    private const string Pkexec = "/usr/bin/pkexec";

    private const string Fallback = "thermi-overlay.service";

    // What a desktop hands its programs to show on it, which a user manager that no graphical session started may not have.
    private static readonly string[] Shown = ["WAYLAND_DISPLAY", "DISPLAY", "XAUTHORITY", "XDG_CURRENT_DESKTOP", "XDG_SESSION_TYPE"];

    // thermi.service's own: back after a crash, a few times a minute at most, never after a desktop it cannot show on, which
    // every start would find the same, and after a package's hand-over.
    private static readonly string[] Restarts =
    [
        "Restart=on-failure", "RestartSec=2", "RestartPreventExitStatus=6", "SuccessExitStatus=75", "RestartForceExitStatus=75",
        "StartLimitIntervalSec=60", "StartLimitBurst=5",
    ];

    /// <summary>
    /// Starts the overlay as the person: its user unit, which runs only in a graphical session systemd knows of; where none
    /// is, as on a Sway or Hyprland that starts no graphical-session.target, <paramref name="thermi"/> as a unit of its own
    /// that sees the window's desktop and comes back if it fails, as the user unit does. True too when that one already
    /// runs; false when neither started.
    /// </summary>
    public static async Task<bool> StartOverlay(string thermi) =>
        await RunAsync(UserUnit.Systemctl, ["--user", "start", UserUnit.Name])
        || await RunAsync(UserUnit.Systemctl, ["--user", "--quiet", "is-active", Fallback])
        || await RunAsync(SystemdRun, [
            "--user", "--collect", "--quiet", $"--unit={Fallback}", "--slice=app.slice", .. Restarts.Select(property => $"--property={property}"),
            .. Shown.Where(name => Environment.GetEnvironmentVariable(name) is not null).Select(name => $"--setenv={name}"),
            thermi]);

    /// <summary>Starts the sensor service's socket behind polkit's prompt: false when the person declined or it failed.</summary>
    public static Task<bool> StartService() => RunAsync(Pkexec, [UserUnit.Systemctl, "start", "thermi-sensors.socket"]);

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
