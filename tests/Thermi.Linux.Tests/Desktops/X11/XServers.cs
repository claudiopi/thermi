using System.Globalization;

namespace Thermi.Linux.Tests.Desktops.X11;

/// <summary>
/// The X servers of a test's own: Xvfb, which KWin and Sway run nested in, each monitor a window of theirs, and Xorg on its
/// dummy driver for an X11 desktop, each monitor an output of its own. Each writes the display it took once it serves it.
/// </summary>
internal static class XServers
{
    /// <summary>Xvfb as wide as the monitors side by side; its one output is the screen.</summary>
    public static async Task StartXvfbAsync(OwnSession session, IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var size = $"{monitors.Sum(monitor => monitor.Width)}x{monitors.Max(monitor => monitor.Height)}x24";
        var dpi = (96 * monitors[0].Scale).ToString("0", CultureInfo.InvariantCulture);
        var server = await session.StartAsync("Xvfb", ["-displayfd", "1", "-screen", "0", size, "-dpi", dpi], cancel: cancel).ConfigureAwait(false);
        session.X11Display = $":{await server.ReadyAsync(cancel).ConfigureAwait(false)}";
    }

    /// <summary>
    /// Xorg with its dummy driver, whose outputs, DUMMY0 and on, are monitors to GTK, which Xvfb's RandR monitors are not. It
    /// runs as the user only where the PC's consoles are closed to them, as they are; it reads no file of the PC's
    /// configuration and no device, and finds no system bus, so it asks logind for nothing; nor does it take a console or
    /// its terminal, or switch the PC's consoles.
    /// </summary>
    public static async Task StartXorgAsync(OwnSession session, IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var (width, height) = (monitors.Sum(monitor => monitor.Width), monitors.Max(monitor => monitor.Height));
        var config = Path.Combine(session.Home, "xorg.conf");
        await File.WriteAllLinesAsync(config, [
            "Section \"ServerFlags\"", "  Option \"AutoAddDevices\" \"false\"", "  Option \"AutoAddGPU\" \"false\"", "EndSection",
            "Section \"Device\"", "  Identifier \"dummy\"", "  Driver \"dummy\"", "  VideoRam 256000", "EndSection",
            "Section \"Monitor\"", "  Identifier \"monitor\"", "  HorizSync 5.0-1000.0", "  VertRefresh 5.0-200.0", "EndSection",
            "Section \"Screen\"", "  Identifier \"screen\"", "  Device \"dummy\"", "  Monitor \"monitor\"", "  DefaultDepth 24",
            "  SubSection \"Display\"", "    Depth 24", $"    Virtual {width} {height}", "  EndSubSection", "EndSection"], cancel).ConfigureAwait(false);
        var none = Directory.CreateDirectory(Path.Combine(session.Home, "xorg.conf.d")).FullName;
        string[] args = ["-config", config, "-configdir", none, "-logfile", Path.Combine(session.Home, "xorg.log"), "-nolisten", "tcp", "-noreset",
            "-s", "0", "-dpms", "-novtswitch", "-keeptty", "-displayfd", "1"];
        var alone = new Dictionary<string, string> { ["DBUS_SYSTEM_BUS_ADDRESS"] = $"unix:path={Path.Combine(session.Home, "no-bus")}" };
        var server = await session.StartAsync("/usr/lib/xorg/Xorg", args, alone, cancel).ConfigureAwait(false);
        session.X11Display = $":{await server.ReadyAsync(cancel).ConfigureAwait(false)}";
        foreach (var (monitor, index) in monitors.Select((monitor, index) => (monitor, index)).Skip(1))
        {
            await session.RunAsync("xrandr", ["--addmode", $"DUMMY{index}", $"{monitor.Width}x{monitor.Height}"], cancel: cancel).ConfigureAwait(false);
        }

        await SetOutputsAsync(session, monitors, swapped: false, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Lays the monitors out side by side as Xorg's outputs, each at its size, from the left, or from the right when swapped:
    /// each keeps its connector, the first primary, as a person's monitors do when they move.
    /// </summary>
    public static async Task SetOutputsAsync(OwnSession session, IReadOnlyList<TestMonitor> monitors, bool swapped, CancellationToken cancel)
    {
        var left = 0;
        List<string> args = [];
        foreach (var index in Order(monitors, swapped))
        {
            var monitor = monitors[index];
            args.AddRange(["--output", $"DUMMY{index}", "--mode", $"{monitor.Width}x{monitor.Height}", "--pos", $"{left}x0"]);
            args.AddRange(index == 0 ? ["--primary"] : []);
            left += monitor.Width;
        }

        var set = await session.RunAsync("xrandr", args, cancel: cancel).ConfigureAwait(false);
        _ = set.Exit == 0 ? 0 : throw new InvalidOperationException($"xrandr placed no monitor: {set.Error}");
        // The dummy driver tells an output connected only as a client probes the outputs, which xrandr did before it set
        // their modes and GTK never does: probed again, every output with a mode is connected, which GTK shows alone.
        await session.RunAsync("xrandr", ["--query"], cancel: cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// The nested compositor's monitors' windows, the X server's only ones, in the order it made them, each moved to where
    /// its monitor is, from the left. KWin names them only by _NET_WM_NAME, which xwininfo reads and xdotool's search does
    /// not; Sway names them not at all.
    /// </summary>
    public static async Task PlaceNestedAsync(OwnSession session, IReadOnlyList<TestMonitor> monitors, bool swapped, CancellationToken cancel)
    {
        var found = await session.RunAsync("xwininfo", ["-root", "-children"], cancel: cancel).ConfigureAwait(false);
        var windows = found.Out.Split('\n')
            .Where(line => line.TrimStart().StartsWith("0x", StringComparison.Ordinal))
            .Select(line => Convert.ToInt64(line.Trim().Split(' ')[0], 16))
            .Order()
            .ToList();
        var left = 0;
        foreach (var index in Order(monitors, swapped))
        {
            var place = await session.RunAsync("xdotool", ["windowmove", $"{windows[index]}", $"{left}", "0"], cancel: cancel).ConfigureAwait(false);
            left += place.Exit == 0 ? monitors[index].Width : throw new InvalidOperationException($"No monitor's window moved: {place.Error}");
        }
    }

    private static List<int> Order(IReadOnlyList<TestMonitor> monitors, bool swapped) =>
        swapped ? [.. Enumerable.Range(0, monitors.Count).Reverse()] : [.. Enumerable.Range(0, monitors.Count)];
}
