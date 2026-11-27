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

    /// <summary>KDE Plasma's KWin on Wayland, with layer surfaces, and a panel of the tests' (<see cref="TestPanel"/>).</summary>
    KWin,

    /// <summary>Sway, wlroots' compositor, with layer surfaces, and its own bar at the top.</summary>
    Sway,

    /// <summary>An X11 desktop: Xvfb, Xfce's window manager and its panel at the top.</summary>
    X11,

    /// <summary>A Wayland compositor with neither GNOME Shell nor layer surfaces: Weston's kiosk shell.</summary>
    BareWayland,
}

/// <summary>A monitor of a test's desktop, its size in pixels, and its scale.</summary>
internal sealed record TestMonitor(int Width, int Height, double Scale = 1);

/// <summary>
/// A desktop of a test's own, started headless in an <see cref="OwnSession"/>, read back through its screenshots and driven
/// through the compositor's own input for tests (<see cref="DesktopInput"/>): a person's desktop is never touched. Each
/// program of the desktop is waited for until it says it is ready, never for a time. Its first monitor is the primary one,
/// where the top bar and the panels are.
/// </summary>
internal sealed class TestDesktop : IAsyncDisposable
{
    // Xfce's panel as a person's first sign-in leaves it, so it asks nothing: one panel at the top of the primary monitor,
    // with the window buttons alone, and no tray of its own, which would take the tests' StatusNotifierWatcher's name.
    private const string XfcePanel = """
        <?xml version="1.0" encoding="UTF-8"?>
        <channel name="xfce4-panel" version="1.0">
          <property name="configver" type="int" value="2"/>
          <property name="panels" type="array">
            <value type="int" value="1"/>
            <property name="panel-1" type="empty">
              <property name="position" type="string" value="p=6;x=0;y=0"/>
              <property name="length" type="uint" value="100"/>
              <property name="position-locked" type="bool" value="true"/>
              <property name="size" type="uint" value="40"/>
              <property name="output-name" type="string" value="Primary"/>
              <property name="plugin-ids" type="array">
                <value type="int" value="1"/>
              </property>
            </property>
          </property>
          <property name="plugins" type="empty">
            <property name="plugin-1" type="string" value="tasklist"/>
          </property>
        </channel>
        """;

    private TestDesktop(DesktopKind kind, OwnSession session, double scale)
    {
        Kind = kind;
        Session = session;
        Scale = scale;
    }

    /// <summary>The name thermi's overlay takes on the session's bus, one per session.</summary>
    public const string ThermiName = "io.github.claudiopi.thermi.Overlay";

    public DesktopKind Kind { get; }

    public OwnSession Session { get; }

    /// <summary>The largest scale of the desktop's monitors, the screenshots' pixels to a logical one.</summary>
    public double Scale { get; }

    /// <summary>The pointer and the keyboard, through the desktop's own input for tests.</summary>
    public DesktopInput Input => new(Kind, Session, Scale);

    public static Task<TestDesktop> StartAsync(DesktopKind kind, IReadOnlyList<TestMonitor> monitors, CancellationToken cancel) =>
        StartAsync(kind, monitors, TestShell.Thermi, cancel);

    /// <summary>A desktop whose GNOME Shell loads <paramref name="extension"/> in place of thermi's, or none when null.</summary>
    public static async Task<TestDesktop> StartAsync(DesktopKind kind, IReadOnlyList<TestMonitor> monitors, string? extension, CancellationToken cancel)
    {
#pragma warning disable CA2000 // The desktop owns the session, and ends it with itself, as when its start fails below.
        var desktop = new TestDesktop(kind, new OwnSession(), monitors.Max(monitor => monitor.Scale));
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
    public Task<SessionProcess> StartThermiAsync(IEnumerable<string> args, CancellationToken cancel) =>
        Session.StartAsync(Built.Program("thermi"), args, cancel: cancel);

    /// <summary>A window of another program under the overlay, maximized or full screen.</summary>
    public Task<TestWindow> OpenWindowAsync(bool fullScreen, CancellationToken cancel) => TestWindow.OpenAsync(Session, fullScreen, cancel);

    /// <summary>
    /// thermi up with <c>--trace</c>, once its overlays are on screen as painted, which it says (<see cref="Traced.Shown"/>):
    /// the test waits for that, and reads the rest of its trace from the process.
    /// </summary>
    public async Task<SessionProcess> ShowThermiAsync(IEnumerable<string> args, CancellationToken cancel)
    {
        var thermi = await StartThermiAsync(["--trace", .. args], cancel).ConfigureAwait(false);
        await Traced.FirstAsync(thermi.Error, Traced.Shown, cancel).ConfigureAwait(false);
        return thermi;
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
                await SetX11MonitorsAsync(monitors, swapped: true, cancel).ConfigureAwait(false);
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
    /// The picture <c>thermi preview</c> paints of the session's first overlay at a scale, for a snapshot file, or for none,
    /// an empty file, as the overlay shows when the sensors stopped.
    /// </summary>
    public async Task<string> PreviewAsync(string? snapshot, double scale, CancellationToken cancel, string? settings = null)
    {
        var picture = Path.Combine(Session.Home, $"preview-{Guid.NewGuid():N}.png");
        if (snapshot is null)
        {
            snapshot = Path.Combine(Session.Home, "none.json");
            await File.WriteAllTextAsync(snapshot, "", cancel).ConfigureAwait(false);
        }

        var painted = await Session.RunAsync(
            Built.Program("thermi"),
            ["preview", snapshot, picture, scale.ToString(CultureInfo.InvariantCulture), settings ?? Settings.PathOf(Session)],
            cancel: cancel).ConfigureAwait(false);
        return painted.Exit == 0 ? picture : throw new InvalidOperationException($"thermi preview failed: {painted.Error}");
    }

    /// <summary>The session's settings file, as thermi reads it, holding <paramref name="config"/>.</summary>
    public Task WriteSettingsAsync(ThermiConfig config, CancellationToken cancel) => Settings.WriteAsync(Session, config, cancel);

    /// <summary>The whole screen as a PNG file of the session's, through the desktop's own screenshot, without the pointer.</summary>
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

    // gdbus returns once the name has an owner on the session's bus, as a program that is up takes it; and when the program
    // that would take it ends first, the wait fails with what it said.
    private async Task WaitForNameAsync(string name, CancellationToken cancel, SessionProcess? owner = null)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var waiting = Session.RunAsync("gdbus", ["wait", "--session", "--timeout", Bound.LineSeconds, name], cancel: done.Token);
        var ended = owner?.WaitForExitAsync(done.Token) ?? waiting;
        var first = await Task.WhenAny(waiting, ended).ConfigureAwait(false);
        await done.CancelAsync().ConfigureAwait(false);
        if (first != waiting)
        {
            var said = await owner!.Error.ToEndAsync(cancel).ConfigureAwait(false);
            throw new InvalidOperationException($"{name} never came on the session's bus: its program ended, {said}");
        }

        var waited = await waiting.ConfigureAwait(false);
        if (waited.Exit != 0)
        {
            throw new InvalidOperationException($"{name} never came on the session's bus: {waited.Error}");
        }
    }

    // On X11 the Shell takes Xvfb's screen, whose monitors are its own; on Wayland, its virtual monitors.
    private async Task StartGnomeAsync(IReadOnlyList<TestMonitor> monitors, string? extension, CancellationToken cancel)
    {
        // The Shell takes the desktop's notifications through a service of its own, which the session's bus starts for it.
        Session.Activates("org.gnome.Shell.Notifications");
        await TestShell.InstallAsync(Session, extension, cancel).ConfigureAwait(false);
        string[] shell = ["--headless", "--wayland", "--wayland-display", OwnSession.WaylandDisplay,
            .. monitors.SelectMany(monitor => new[] { "--virtual-monitor", $"{monitor.Width}x{monitor.Height}" })];
        if (Kind == DesktopKind.GnomeX11)
        {
            var options = await Session.RunAsync("gnome-shell", ["--help-all"], cancel: cancel).ConfigureAwait(false);
            Assert.SkipUnless(options.Out.Contains(" --x11 ", StringComparison.Ordinal), "This GNOME Shell runs on no X11: GNOME 50 removed it.");
            await StartXvfbAsync(monitors, cancel).ConfigureAwait(false);
            shell = ["--x11"];
        }

        var started = await Session.StartAsync("gnome-shell", shell, cancel: cancel).ConfigureAwait(false);
        await WaitForNameAsync(TestShell.ReadyName, cancel, started).ConfigureAwait(false);
        // A headless Shell's seat has no pointer or keyboard until the tests' own: made now, before any program starts, so
        // each finds them there, and the first click is not lost while a window takes up a pointer just added.
        await Input.ShellAsync("", cancel).ConfigureAwait(false);
        if (monitors.Count > 1 || monitors.Any(monitor => Math.Abs(monitor.Scale - 1) > 0.01))
        {
            await ConfigureGnomeAsync(monitors, swapped: false, cancel).ConfigureAwait(false);
        }
    }

    // Each monitor scaled and placed as GNOME's Settings does it, the first primary, through Mutter's own display
    // configuration, called from inside the Shell, which serves it, so the call cannot wait for itself: it takes a bus name
    // of its own once Mutter applied it.
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
    // the person once it checks no permission, as its own tests run it. A bare KWin has no panel: the tests show their own.
    private async Task StartKWinAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var first = monitors[0];
        var kwin = await Session.StartAsync(
            "kwin_wayland",
            ["--virtual", "--xwayland", "--socket", OwnSession.WaylandDisplay, "--width", $"{first.Width}", "--height", $"{first.Height}",
                "--scale", first.Scale.ToString(CultureInfo.InvariantCulture), "--output-count", $"{monitors.Count}", "printenv", "DISPLAY"],
            new Dictionary<string, string> { ["KWIN_WAYLAND_NO_PERMISSION_CHECKS"] = "1" },
            cancel).ConfigureAwait(false);
        Session.X11Display = await kwin.ReadyAsync(cancel).ConfigureAwait(false);
        await TestPanel.ShowAsync(Session, cancel).ConfigureAwait(false);
    }

    // Sway headless, one output per monitor at its scale, side by side, and its bar at the top of the first; its config's
    // last line says it is ready.
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
        string[] bar = ["bar {", "  position top", "  output HEADLESS-1", "}"];
        await File.WriteAllLinesAsync(config, [.. outputs, .. bar, "exec echo ready"], cancel).ConfigureAwait(false);
        // wlroots draws to no screen and reads no device of the PC's.
        var headless = new Dictionary<string, string> { ["WLR_BACKENDS"] = "headless", ["WLR_LIBINPUT_NO_DEVICES"] = "1" };
        await (await Session.StartAsync("sway", ["--config", config], headless, cancel).ConfigureAwait(false)).ReadyAsync(cancel).ConfigureAwait(false);
    }

    private async Task StartX11Async(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var channels = Directory.CreateDirectory(Path.Combine(Session.Variables["XDG_CONFIG_HOME"], "xfce4", "xfconf", "xfce-perchannel-xml"));
        await File.WriteAllTextAsync(Path.Combine(channels.FullName, "xfce4-panel.xml"), XfcePanel, cancel).ConfigureAwait(false);
        // Xfce's programs keep their settings in xfconfd, which the session's bus starts when they ask.
        Session.Activates("org.xfce.Xfconf");
        await StartXvfbAsync(monitors, cancel).ConfigureAwait(false);
        await Session.StartAsync("xfwm4", [], cancel: cancel).ConfigureAwait(false);
        await Session.StartAsync("xfce4-panel", [], cancel: cancel).ConfigureAwait(false);
        await WaitForNameAsync("org.xfce.Panel", cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Lays the monitors out side by side as RandR monitors of the X server's one screen, from the left, or from the right
    /// when swapped: each keeps its name, the first primary, as a person's monitors do when they move.
    /// </summary>
    public async Task SetX11MonitorsAsync(IReadOnlyList<TestMonitor> monitors, bool swapped, CancellationToken cancel)
    {
        var order = swapped ? monitors.Select((monitor, index) => index).Reverse().ToList() : [.. Enumerable.Range(0, monitors.Count)];
        var left = 0;
        foreach (var index in order)
        {
            var monitor = monitors[index];
            var geometry = string.Create(CultureInfo.InvariantCulture, $"{monitor.Width}/0x{monitor.Height}/0+{left}+0");
            var name = index == 0 ? "*thermi-0" : $"thermi-{index}";
            var set = await Session.RunAsync("xrandr", ["--setmonitor", name, geometry, "none"], cancel: cancel).ConfigureAwait(false);
            left += set.Exit == 0 ? monitor.Width : throw new InvalidOperationException($"xrandr took no monitor: {set.Error}");
        }
    }

    // Xvfb writes the display it took once it serves it.
    private async Task StartXvfbAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var size = $"{monitors.Sum(monitor => monitor.Width)}x{monitors.Max(monitor => monitor.Height)}x24";
        var dpi = (96 * monitors[0].Scale).ToString("0", CultureInfo.InvariantCulture);
        var server = await Session.StartAsync("Xvfb", ["-displayfd", "1", "-screen", "0", size, "-dpi", dpi], cancel: cancel).ConfigureAwait(false);
        Session.X11Display = $":{await server.ReadyAsync(cancel).ConfigureAwait(false)}";
        await SetX11MonitorsAsync(monitors, swapped: false, cancel).ConfigureAwait(false);
    }

    // Weston's kiosk shell offers neither the Shell's extension nor layer surfaces; the client it starts says it is up.
    private async Task StartWestonAsync(TestMonitor monitor, CancellationToken cancel)
    {
        string[] args = ["--backend=headless", "--shell=kiosk", $"--socket={OwnSession.WaylandDisplay}", $"--width={monitor.Width}", "--", "echo", "ready"];
        await (await Session.StartAsync("weston", args, cancel: cancel).ConfigureAwait(false)).ReadyAsync(cancel).ConfigureAwait(false);
    }
}
