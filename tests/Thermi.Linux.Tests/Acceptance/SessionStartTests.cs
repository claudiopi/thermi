using Thermi.Linux.Agents;
using Thermi.Linux.Gnome;
using Thermi.Linux.Tests.Desktops;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// The overlay at sign-in where the desktop brings no graphical-session.target for systemd, which alone starts
/// <c>thermi.service</c> (<c>docs/specs/006-linux/spec.md</c>, R13): the package's autostart entry, which Xfce, MATE and
/// Cinnamon run as a session starts, and its file of Sway's config.d, which Sway's own config includes.
/// </summary>
public sealed class SessionStartTests
{
    private static readonly string Packaging = Path.Combine(Built.Root, "src", "Thermi.Linux", "Packaging");

    // Runs the entry as a session's start runs its autostart, as GIO reads it: where it shows on the session's desktop.
    private const string Autostart = """
        import sys, gi
        from gi.repository import Gio
        entry = Gio.DesktopAppInfo.new_from_filename(sys.argv[1])
        if entry.get_show_in(sys.argv[2]) and not entry.get_is_hidden():
            entry.launch([], None)
        """;

    // With start at sign-in on, the session's start brings the overlay up, as a unit of its own, since the target never
    // comes; a start again, as a second autostart run, finds it running and starts none.
    [Theory(Timeout = Bound.Test)] // Rows: the autostart Xfce runs, and Sway's config.d.
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSessionsStartBringsTheOverlayUp(bool sway)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(sway ? DesktopKind.Sway : DesktopKind.X11, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        var units = await UserUnits.StartAsync(session, cancel);
        await units.SystemctlAsync(["stop", "graphical-session.target"], cancel);
        await units.SystemctlAsync(["enable", UserUnit.Name], cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);

        await StartSessionAsync(desktop, cancel);
        var up = await session.RunAsync("gdbus", ["wait", "--session", "--timeout", Bound.LineSeconds, ShellLink.Name], cancel: cancel);
        var first = await MainProcessAsync(units, cancel);
        var again = await session.RunAsync(Built.Program("thermi"), [SignIn.Command], cancel: cancel);

        Assert.True(up.Exit == 0, $"The overlay never came up: {up.Error}");
        Assert.Equal(0, again.Exit);
        Assert.Equal(("inactive", first), (await units.SystemctlAsync(["is-active", UserUnit.Name], cancel), await MainProcessAsync(units, cancel)));
        Assert.NotEqual("0", first);
    }

    private static async Task StartSessionAsync(TestDesktop desktop, CancellationToken cancel)
    {
        var session = desktop.Session;
        if (desktop.Kind == DesktopKind.X11)
        {
            var script = Path.Combine(session.Home, "autostart.py");
            await File.WriteAllTextAsync(script, Autostart, cancel);
            var entry = Path.Combine(Packaging, "io.github.claudiopi.thermi-sign-in.desktop");
            var ran = await session.RunAsync("python3", [script, entry, "XFCE"], cancel: cancel);
            Assert.True(ran.Exit == 0, ran.Error);
            return;
        }

        // Each command of the file, through Sway's own socket, as its config runs them as it starts.
        var socket = Directory.GetFiles(session.RuntimeFolder, "sway-ipc.*.sock").Single();
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(Packaging, "60-thermi.conf"), cancel))
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                var ran = await session.RunAsync("swaymsg", [line], new Dictionary<string, string> { ["SWAYSOCK"] = socket }, cancel);
                Assert.True(ran.Exit == 0, ran.Error + ran.Out);
            }
        }
    }

    // The overlay's own unit, which the window starts too where the target never came.
    private static Task<string> MainProcessAsync(UserUnits units, CancellationToken cancel) =>
        units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi-overlay.service"], cancel);
}
