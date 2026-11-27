using System.Diagnostics;
using Thermi.Core.Bridge;

namespace Thermi.Linux.Window;

/// <summary>
/// What the window fixes on Linux, each behind the desktop's own prompt: thermi's GNOME Shell extension, installed from
/// extensions.gnome.org through the Shell, which asks the person and loads it at once, or turned on; and the stopped
/// sensor service, whose socket polkit's prompt starts. thermi itself never runs as root. And starting at sign-in, which
/// is the overlay's user unit being enabled (docs/adr/0019-linux.md).
/// </summary>
internal static class LinuxFixes
{
    /// <summary>The extension's id, as its <c>metadata.json</c> in <c>src/Thermi.Gnome</c> says.</summary>
    public const string Extension = "thermi@claudiopi.github.io";

    /// <summary>The overlay's user unit, which the desktop's session starts at sign-in once enabled.</summary>
    public const string OverlayUnit = "thermi.service";

    private const string Shell = "org.gnome.Shell";
    private const string ShellPath = "/org/gnome/Shell";
    private const string Extensions = "org.gnome.Shell.Extensions";
    private const string Systemctl = "/usr/bin/systemctl";
    private const string Pkexec = "/usr/bin/pkexec";

    // GNOME Shell's own numbers for an extension's state; one it lacks is not installed.
    private const double Enabled = 1;
    private const double Active = 8;

    /// <summary>
    /// Where thermi's extension is in a GNOME session: missing, there but off, or on; null outside GNOME or where the Shell
    /// does not answer, which has no extension to fix.
    /// </summary>
    public static ExtensionState? ExtensionState(Gio.DBusConnection? bus)
    {
        if (bus is null || !(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var answer = bus.CallSync(
                Shell, ShellPath, Extensions, "GetExtensionInfo", GLib.Variant.NewTuple([GLib.Variant.NewString(Extension)]), null, Gio.DBusCallFlags.None,
                2000, null);
            using var info = answer.GetChildValue(0);
            if (info.NChildren() == 0)
            {
                return Core.Bridge.ExtensionState.Missing;
            }

            using var state = info.LookupValue("state", null);
            return state?.GetDouble() is Enabled or Active ? Core.Bridge.ExtensionState.Enabled : Core.Bridge.ExtensionState.Disabled;
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    /// <summary>
    /// Installs the extension from extensions.gnome.org behind GNOME's prompt, or turns it on: true once the Shell says
    /// it runs; false when the person declined, the Shell failed or there was nothing to fix.
    /// </summary>
    public static async Task<bool> InstallExtension(Gio.DBusConnection? bus, ExtensionState? state)
    {
        if (bus is null || state is not (Core.Bridge.ExtensionState.Missing or Core.Bridge.ExtensionState.Disabled))
        {
            return false;
        }

        var method = state == Core.Bridge.ExtensionState.Missing ? "InstallRemoteExtension" : "EnableExtension";
        try
        {
            using var answer = await bus.CallAsync(Shell, ShellPath, Extensions, method, GLib.Variant.NewTuple([GLib.Variant.NewString(Extension)]));
        }
        catch (GLib.GException)
        {
            // Declined, offline, or the prompt outlasted the call: what the Shell has now says which.
        }

        return ExtensionState(bus) == Core.Bridge.ExtensionState.Enabled;
    }

    /// <summary>Starts the sensor service's socket behind polkit's prompt: false when the person declined or it failed.</summary>
    public static Task<bool> StartService() => RunAsync(Pkexec, [Systemctl, "start", "thermi-sensors.socket"]);

    /// <summary>Whether the overlay's user unit starts at sign-in.</summary>
    public static bool StartsAtSignIn() => Run(Systemctl, ["--user", "--quiet", "is-enabled", OverlayUnit]);

    public static void StartAtSignIn(bool on) => _ = Run(Systemctl, ["--user", "--quiet", on ? "enable" : "disable", OverlayUnit]);

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

    private static bool Run(string program, IReadOnlyList<string> args)
    {
        try
        {
            using var process = Process.Start(Info(program, args));
            process?.WaitForExit();
            return process?.ExitCode == 0;
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
