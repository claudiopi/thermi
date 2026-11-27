using System.Diagnostics;
using System.Globalization;
using Thermi.Core.Settings;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>The desktops thermi shows on, as the tests start them headless.</summary>
internal enum DesktopKind
{
    /// <summary>GNOME Shell on Wayland, where thermi's extension shows the overlay.</summary>
    Gnome,

    /// <summary>GNOME Shell on X11, on Xvfb, as GNOME 46 to 48 still sign in.</summary>
    GnomeX11,

    /// <summary>KDE Plasma's KWin on Wayland, with layer surfaces.</summary>
    KWin,

    /// <summary>Sway, wlroots' compositor, with layer surfaces.</summary>
    Sway,

    /// <summary>An X11 desktop: Xvfb, Xfce's window manager and its panel.</summary>
    X11,

    /// <summary>A Wayland compositor with neither GNOME Shell nor layer surfaces: Weston's kiosk shell.</summary>
    BareWayland,
}

/// <summary>A monitor of a test's desktop, its size in pixels, and its scale.</summary>
internal sealed record TestMonitor(int Width, int Height, double Scale = 1);

/// <summary>
/// A desktop of a test's own, started headless in an <see cref="OwnSession"/>, read back through its screenshots and driven
/// through the compositor's own input for tests (<see cref="DesktopInput"/>): a person's desktop is never touched. Each
/// program of the desktop is waited for until it says it is ready, never for a time.
/// </summary>
internal sealed class TestDesktop : IAsyncDisposable
{
    private TestDesktop(DesktopKind kind, OwnSession session)
    {
        Kind = kind;
        Session = session;
    }

    /// <summary>The name thermi's overlay takes on the session's bus, as one GTK application per session does.</summary>
    public const string ThermiName = "io.github.claudiopi.thermi";

    public DesktopKind Kind { get; }

    public OwnSession Session { get; }

    /// <summary>The pointer and the keyboard, through the desktop's own input for tests.</summary>
    public DesktopInput Input => new(Kind, Session);

    public static Task<TestDesktop> StartAsync(DesktopKind kind, IReadOnlyList<TestMonitor> monitors, CancellationToken cancel) =>
        StartAsync(kind, monitors, TestShell.Thermi, cancel);

    /// <summary>A desktop whose GNOME Shell loads <paramref name="extension"/> in place of thermi's, or none when null.</summary>
    public static async Task<TestDesktop> StartAsync(DesktopKind kind, IReadOnlyList<TestMonitor> monitors, string? extension, CancellationToken cancel)
    {
#pragma warning disable CA2000 // The desktop owns the session, and ends it with itself, as when its start fails below.
        var desktop = new TestDesktop(kind, new OwnSession());
#pragma warning restore CA2000
        try
        {
            await (kind switch
            {
                DesktopKind.Gnome or DesktopKind.GnomeX11 => desktop.StartGnomeAsync(monitors, extension, cancel),
                DesktopKind.KWin => desktop.StartKWinAsync(monitors, cancel),
                DesktopKind.Sway => desktop.StartSwayAsync(monitors, cancel),
                DesktopKind.X11 => desktop.StartX11Async(monitors, cancel),
                DesktopKind.BareWayland => desktop.StartWestonAsync(monitors[0], cancel),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown desktop."),
            }).ConfigureAwait(false);
            return desktop;
        }
        catch
        {
            await desktop.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Starts thermi in the desktop's session, as its user unit does, with <paramref name="args"/>.</summary>
    public Task<Process> StartThermiAsync(IEnumerable<string> args, CancellationToken cancel) =>
        Session.StartAsync(Built.Program("thermi"), args, cancel: cancel);

    /// <summary>A window of another program under the overlay, maximized or full screen.</summary>
    public Task<TestWindow> OpenWindowAsync(bool fullScreen, CancellationToken cancel) => TestWindow.OpenAsync(Session, fullScreen, cancel);

    /// <summary>
    /// thermi up and its overlays painted: thermi's <c>--trace</c> writes each paint to its standard error too, so the test
    /// waits for the paint itself.
    /// </summary>
    public async Task<Process> ShowThermiAsync(IEnumerable<string> args, CancellationToken cancel)
    {
        var thermi = await StartThermiAsync(["--trace", .. args], cancel).ConfigureAwait(false);
        await PaintedAsync(thermi, cancel).ConfigureAwait(false);
        return thermi;
    }

    /// <summary>Returns once thermi, started with <c>--trace</c>, painted next, a hidden or faded overlay's paint too.</summary>
    public static async Task PaintedAsync(Process thermi, CancellationToken cancel)
    {
        while (await thermi.StandardError.ReadLineAsync(cancel).ConfigureAwait(false) is { } line)
        {
            if (line.EndsWith("paint", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException("thermi ended before it painted.");
    }

    /// <summary>Swaps the two monitors' places, left for right, as a person does in the desktop's display settings.</summary>
    public async Task SwapMonitorsAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        switch (Kind)
        {
            case DesktopKind.Gnome or DesktopKind.GnomeX11:
                await ConfigureGnomeAsync(monitors, swapped: true, cancel).ConfigureAwait(false);
                break;
            case DesktopKind.X11:
                await SetX11MonitorsAsync([.. monitors.Reverse()], cancel).ConfigureAwait(false);
                break;
            case DesktopKind.KWin:
                var moved = await Session.RunAsync(
                    "kscreen-doctor", [$"output.1.position.{monitors[1].Width},0", "output.2.position.0,0"], cancel: cancel).ConfigureAwait(false);
                _ = moved.Exit == 0 ? 0 : throw new InvalidOperationException($"KWin moved no monitor: {moved.Error}");
                break;
            case DesktopKind.Sway or DesktopKind.BareWayland:
            default:
                throw new NotSupportedException($"{Kind}'s monitors stay where they are.");
        }
    }

    /// <summary>
    /// The picture <c>thermi preview</c> paints of the session's first overlay at a scale, for a recorded tree's snapshot, or
    /// for none, an empty file, as the overlay shows when the sensors stopped.
    /// </summary>
    public async Task<string> PreviewAsync(string? tree, double scale, CancellationToken cancel)
    {
        var picture = Path.Combine(Session.Home, $"preview-{Guid.NewGuid():N}.png");
        var snapshot = tree is null ? Path.Combine(Session.Home, "none.json") : Built.Tree(tree) + ".json";
        if (tree is null)
        {
            await File.WriteAllTextAsync(snapshot, "", cancel).ConfigureAwait(false);
        }

        var painted = await Session.RunAsync(
            Built.Program("thermi"),
            ["preview", snapshot, picture, scale.ToString(CultureInfo.InvariantCulture), Settings.PathOf(Session)],
            cancel: cancel).ConfigureAwait(false);
        return painted.Exit == 0 ? picture : throw new InvalidOperationException($"thermi preview failed: {painted.Error}");
    }

    /// <summary>The session's settings file, as thermi reads it, holding <paramref name="config"/>.</summary>
    public Task WriteSettingsAsync(ThermiConfig config, CancellationToken cancel) => Settings.WriteAsync(Session, config, cancel);

    /// <summary>The whole screen as a PNG file of the session's, through the desktop's own screenshot.</summary>
    public async Task<string> ScreenshotAsync(CancellationToken cancel)
    {
        var file = Path.Combine(Session.Home, $"screen-{Guid.NewGuid():N}.png");
        (string Program, string[] Args) shot = Kind switch
        {
            DesktopKind.Gnome or DesktopKind.GnomeX11 =>
                ("gdbus", Call("org.gnome.Shell.Screenshot", "/org/gnome/Shell/Screenshot", "Screenshot", "false", "false", $"'{file}'")),
            DesktopKind.KWin => ("spectacle", ["--background", "--nonotify", "--fullscreen", "--output", file]),
            DesktopKind.Sway => ("grim", [file]),
            DesktopKind.X11 => ("import", ["-window", "root", file]),
            DesktopKind.BareWayland => throw new NotSupportedException("Weston's kiosk shell takes no screenshot for tests."),
            _ => throw new InvalidOperationException($"Unknown desktop {Kind}."),
        };
        var (program, args) = shot;
        var ran = await Session.RunAsync(program, args, cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? file : throw new InvalidOperationException($"No screenshot of {Kind}: {ran.Error}");
    }

    public async ValueTask DisposeAsync() => await Session.DisposeAsync().ConfigureAwait(false);

    internal static string[] Call(string destination, string path, string method, params string[] args) =>
        ["call", "--session", "--dest", destination, "--object-path", path, "--method", $"{destination}.{method}", .. args];

    // gdbus returns once the name has an owner on the session's bus, as a program that is up takes it.
    private async Task WaitForNameAsync(string name, CancellationToken cancel)
    {
        var waited = await Session.RunAsync("gdbus", ["wait", "--session", name], cancel: cancel).ConfigureAwait(false);
        if (waited.Exit != 0)
        {
            throw new InvalidOperationException($"{name} never came on the session's bus: {waited.Error}");
        }
    }

    // A line the program writes once it is ready.
    private static async Task ReadyAsync(Process process, CancellationToken cancel)
    {
        if (await process.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false) is null)
        {
            var error = await process.StandardError.ReadToEndAsync(cancel).ConfigureAwait(false);
            throw new InvalidOperationException($"{process.StartInfo.FileName} ended before it was ready: {error}");
        }
    }

    // On X11 the Shell takes Xvfb's screen, whose monitors are its own; on Wayland, its virtual monitors.
    private async Task StartGnomeAsync(IReadOnlyList<TestMonitor> monitors, string? extension, CancellationToken cancel)
    {
        await TestShell.InstallAsync(Session, extension, cancel).ConfigureAwait(false);
        string[] shell = ["--headless", "--wayland", "--wayland-display", OwnSession.WaylandDisplay,
            .. monitors.SelectMany(monitor => new[] { "--virtual-monitor", $"{monitor.Width}x{monitor.Height}" })];
        if (Kind == DesktopKind.GnomeX11)
        {
            await StartXvfbAsync(monitors, cancel).ConfigureAwait(false);
            shell = ["--x11"];
        }

        await Session.StartAsync("gnome-shell", shell, cancel: cancel).ConfigureAwait(false);
        await WaitForNameAsync(TestShell.ReadyName, cancel).ConfigureAwait(false);
        if (monitors.Any(monitor => Math.Abs(monitor.Scale - 1) > 0.01))
        {
            await ConfigureGnomeAsync(monitors, swapped: false, cancel).ConfigureAwait(false);
        }
    }

    // Each monitor scaled and placed as GNOME's Settings does it, through Mutter's own display configuration, called from
    // inside the Shell, which serves it, so the call cannot wait for itself: it takes a bus name of its own once Mutter
    // applied it.
    private async Task ConfigureGnomeAsync(IReadOnlyList<TestMonitor> monitors, bool swapped, CancellationToken cancel)
    {
        var applied = $"{TestShell.ReadyName}.Applied{Guid.NewGuid():N}";
        var scales = string.Join(',', monitors.Select(monitor => monitor.Scale.ToString(CultureInfo.InvariantCulture)));
        await Input.ShellAsync(
            $"const scales = [{scales}]; const swapped = {(swapped ? "true" : "false")};"
                + " const call = (method, args, then) => Gio.DBus.session.call('org.gnome.Mutter.DisplayConfig',"
                + " '/org/gnome/Mutter/DisplayConfig', 'org.gnome.Mutter.DisplayConfig', method, args, null, 0, -1, null,"
                + " (bus, result) => then(bus.call_finish(result).recursiveUnpack()));"
                + " call('GetCurrentState', null, ([serial, monitors]) => { const at = []; let x = 0;"
                + " const modes = monitors.map(([, modes]) => modes.find(mode => mode[6]['is-current']));"
                + " for (const index of swapped ? [...monitors.keys()].reverse() : monitors.keys()) {"
                + " at[index] = x; x += Math.round(modes[index][1] / scales[index]); }"
                + " const logical = monitors.map(([[connector]], index) => [at[index], 0, scales[index], 0, index === 0, [[connector, modes[index][0], {}]]]);"
                + " call('ApplyMonitorsConfig', new GLib.Variant('(uua(iiduba(ssa{sv}))a{sv})', [serial, 1, logical, {}]),"
                + $" () => Gio.bus_own_name(Gio.BusType.SESSION, '{applied}', 0, null, null, null)); }});",
            cancel).ConfigureAwait(false);
        await WaitForNameAsync(applied, cancel).ConfigureAwait(false);
    }

    // KWin starts the program it is given once Wayland and Xwayland serve, with the X11 display it took; the tests' input
    // reaches KWin through that Xwayland's XTEST, and its screenshots through Spectacle, which KWin lets in without asking
    // the person once it checks no permission, as its own tests run it.
    private async Task StartKWinAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var first = monitors[0];
        var kwin = await Session.StartAsync(
            "kwin_wayland",
            ["--virtual", "--xwayland", "--socket", OwnSession.WaylandDisplay, "--width", $"{first.Width}", "--height", $"{first.Height}",
                "--scale", first.Scale.ToString(CultureInfo.InvariantCulture), "--output-count", $"{monitors.Count}", "printenv", "DISPLAY"],
            new Dictionary<string, string> { ["KWIN_WAYLAND_NO_PERMISSION_CHECKS"] = "1" },
            cancel).ConfigureAwait(false);
        Session.X11Display = await kwin.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"KWin ended before it was ready: {await kwin.StandardError.ReadToEndAsync(cancel).ConfigureAwait(false)}");
    }

    // Sway headless, one output per monitor at its scale, side by side; its config's last line says it is ready.
    private async Task StartSwayAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var config = Path.Combine(Session.Home, "sway.config");
        var left = 0;
        var outputs = monitors.Select((monitor, index) =>
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"output HEADLESS-{index + 1} mode {monitor.Width}x{monitor.Height} position {left} 0 scale {monitor.Scale}");
            left += (int)(monitor.Width / monitor.Scale);
            return line;
        });
        await File.WriteAllLinesAsync(config, [.. outputs, "exec echo ready"], cancel).ConfigureAwait(false);
        // wlroots draws to no screen and reads no device of the PC's.
        var headless = new Dictionary<string, string> { ["WLR_BACKENDS"] = "headless", ["WLR_LIBINPUT_NO_DEVICES"] = "1" };
        await ReadyAsync(await Session.StartAsync("sway", ["--config", config], headless, cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
    }

    private async Task StartX11Async(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        await StartXvfbAsync(monitors, cancel).ConfigureAwait(false);
        await Session.StartAsync("xfwm4", [], cancel: cancel).ConfigureAwait(false);
        await Session.StartAsync("xfce4-panel", [], cancel: cancel).ConfigureAwait(false);
        await WaitForNameAsync("org.xfce.Panel", cancel).ConfigureAwait(false);
    }

    /// <summary>Lays the monitors out side by side, from the left, as RandR monitors of the X server's one screen.</summary>
    public async Task SetX11MonitorsAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var left = 0;
        foreach (var (monitor, index) in monitors.Select((monitor, index) => (monitor, index)))
        {
            var geometry = string.Create(CultureInfo.InvariantCulture, $"{monitor.Width}/0x{monitor.Height}/0+{left}+0");
            var set = await Session.RunAsync("xrandr", ["--setmonitor", $"thermi-{index}", geometry, "none"], cancel: cancel).ConfigureAwait(false);
            left += set.Exit == 0 ? monitor.Width : throw new InvalidOperationException($"xrandr took no monitor: {set.Error}");
        }
    }

    // Xvfb writes the display it took once it serves it.
    private async Task StartXvfbAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var size = $"{monitors.Sum(monitor => monitor.Width)}x{monitors.Max(monitor => monitor.Height)}x24";
        var dpi = (96 * monitors[0].Scale).ToString("0", CultureInfo.InvariantCulture);
        var server = await Session.StartAsync("Xvfb", ["-displayfd", "1", "-screen", "0", size, "-dpi", dpi], cancel: cancel).ConfigureAwait(false);
        Session.X11Display = $":{await server.StandardOutput.ReadLineAsync(cancel).ConfigureAwait(false)}";
        await SetX11MonitorsAsync(monitors, cancel).ConfigureAwait(false);
    }

    // Weston's kiosk shell offers neither the Shell's extension nor layer surfaces; the client it starts says it is up.
    private async Task StartWestonAsync(TestMonitor monitor, CancellationToken cancel)
    {
        string[] args = ["--backend=headless", "--shell=kiosk", $"--socket={OwnSession.WaylandDisplay}", $"--width={monitor.Width}", "--", "echo", "ready"];
        await ReadyAsync(await Session.StartAsync("weston", args, cancel: cancel).ConfigureAwait(false), cancel).ConfigureAwait(false);
    }
}
