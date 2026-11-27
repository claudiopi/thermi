using System.Text.RegularExpressions;
using Thermi.Core.Settings;

namespace Thermi.Linux.Tests.Desktops.Surfaces;

/// <summary>
/// What a desktop off GNOME changes under a running overlay, which thermi must follow as GNOME's extension follows it: a
/// panel that starts after thermi, a window that goes full screen, a panel raised above it, and the window manager's close.
/// </summary>
public sealed partial class SurfaceTests
{
    // The most of thermi's seconds a change of the desktop takes to reach the overlay: it comes within a frame or two.
    private const int Seconds = 10;

    private static readonly TestMonitor Monitor = new(1920, 1080);

    public enum PanelCase
    {
        KWin,
        Sway,
    }

    public enum X11Case
    {
        FullScreen,
        PanelRaised,
        Closed,
    }

    // A panel that maps after thermi, as plasmashell or a bar Sway's config starts, takes the overlay snapped to the panels,
    // which shows on it and above it, though the compositor stacked the panel above thermi's surfaces as it came.
    [Theory(Timeout = Bound.Test)]
    [InlineData(PanelCase.KWin)]
    [InlineData(PanelCase.Sway)]
    public async Task APanelThatStartsAfterThermiTakesTheOverlay(PanelCase row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(row == PanelCase.KWin ? DesktopKind.KWin : DesktopKind.Sway, [Monitor], cancel, panel: null);
        // A second overlay over the windows, which shows from the start, so thermi paints each second until the panel comes.
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.PanelOverlay, Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        var snapshot = (await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel)).Snapshot;
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var picture = await desktop.PreviewAsync(snapshot, 1, cancel);

        await TestPanel.ShowAsync(desktop.Session, PanelEdge.Bottom, cancel);

        Assert.True(await WithinAsync(thermi, async () => Taskbar().IsMatch(await MonitorsAsync(desktop, cancel)), cancel));
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        var shown = await desktop.ScreenshotAsync(cancel);
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        var alone = await desktop.ScreenshotAsync(cancel);
        var (_, height) = await Pictures.SizeAsync(desktop.Session, picture, cancel);
        var fewest = long.MaxValue;
        for (var y = Monitor.Height - TestPanel.Height - height; y <= Monitor.Height - height && fewest > 0; y++)
        {
            fewest = Math.Min(fewest, await Pictures.DifferingAsync(desktop.Session, alone, shown, picture, Scene.OnPanel.X, y, cancel));
        }

        Assert.Equal(0, fewest);
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(X11Case.FullScreen)]
    [InlineData(X11Case.PanelRaised)]
    [InlineData(X11Case.Closed)]
    public async Task TheWindowManagerReachesTheOverlayOnX11(X11Case row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [Monitor], cancel);
        var overlay = row == X11Case.FullScreen
            ? Scene.Overlay with { Position = Scene.OnWindow, Visibility = Visibility.OnlyInFullscreen }
            : Scene.PanelOverlay;
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay] }, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        switch (row)
        {
            case X11Case.FullScreen:
                // Shown only while a window covers its monitor full screen, which the window manager says of the active one.
                Assert.Empty(await ThermisWindowsAsync(desktop, thermi, cancel));
                await desktop.OpenWindowAsync(fullScreen: true, cancel);
                // The full-screen window covers it, as on every X11 desktop, so it draws no frame to wait on: xdotool waits
                // for its window to map.
                Assert.NotEmpty(await ThermisWindowsAsync(desktop, thermi, cancel, sync: true));
                break;
            case X11Case.PanelRaised:
                var panels = await desktop.Session.RunAsync("xdotool", ["search", "--onlyvisible", "--class", "xfce4-panel"], cancel: cancel);
                var panel = panels.Out.Split('\n')[0];
                var ours = (await ThermisWindowsAsync(desktop, thermi, cancel))[0];
                await desktop.Session.RunAsync("xdotool", ["windowraise", panel], cancel: cancel);
                Assert.True(await WithinAsync(thermi, async () => await AboveAsync(desktop, ours, panel, cancel), cancel));
                break;
            case X11Case.Closed:
                // As a pager or a task switcher closes a window: the window manager asks it to close, and thermi quits.
                await CloseAsync(desktop, (await ThermisWindowsAsync(desktop, thermi, cancel))[0], cancel);
                await thermi.WaitForExitAsync(cancel);
                Assert.Equal(0, thermi.ExitCode);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case.");
        }
    }

    // Whether what the desktop changed reached the overlay, checked after each of thermi's seconds, up to Seconds of them.
    private static async Task<bool> WithinAsync(SessionProcess thermi, Func<Task<bool>> reached, CancellationToken cancel)
    {
        for (var second = 0; second < Seconds; second++)
        {
            if (await reached())
            {
                return true;
            }

            await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        }

        return await reached();
    }

    private static async Task<string> MonitorsAsync(TestDesktop desktop, CancellationToken cancel) =>
        (await desktop.Session.RunAsync(Built.Program("thermi"), ["monitors"], cancel: cancel)).Out;

    // thermi's windows on screen, or with sync, once one is. xdotool walks the X server's windows, and ends at a window
    // that went as it walked, as a window that opens and its own short-lived ones do: that says nothing of thermi's, so
    // it walks again.
    private static async Task<string[]> ThermisWindowsAsync(TestDesktop desktop, SessionProcess thermi, CancellationToken cancel, bool sync = false)
    {
        string[] search = ["search", .. sync ? ["--sync"] : Array.Empty<string>(), "--onlyvisible", "--pid", $"{thermi.Id}"];
        var found = await desktop.Session.RunAsync("xdotool", search, cancel: cancel);
        while (found.Error.Contains("BadWindow", StringComparison.Ordinal))
        {
            found = await desktop.Session.RunAsync("xdotool", search, cancel: cancel);
        }

        return found.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    // Whether the window manager stacks thermi's window above the panel, bottom to top as it lists them.
    private static async Task<bool> AboveAsync(TestDesktop desktop, string ours, string panel, CancellationToken cancel)
    {
        var said = (await desktop.Session.RunAsync("xprop", ["-root", "_NET_CLIENT_LIST_STACKING"], cancel: cancel)).Out;
        var stacked = Window().Matches(said).Select(match => Convert.ToInt64(match.Value, 16)).ToList();
        return stacked.IndexOf(long.Parse(ours, System.Globalization.CultureInfo.InvariantCulture))
            > stacked.IndexOf(long.Parse(panel, System.Globalization.CultureInfo.InvariantCulture));
    }

    // WM_DELETE_WINDOW, sent as the window manager sends it, from Python's ctypes over Xlib: the desktops here ship no tool
    // that sends it.
    private static async Task CloseAsync(TestDesktop desktop, string window, CancellationToken cancel)
    {
        const string Code = """
            import ctypes, sys
            x = ctypes.cdll.LoadLibrary('libX11.so.6')
            x.XOpenDisplay.restype = ctypes.c_void_p
            x.XInternAtom.restype = ctypes.c_ulong
            x.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
            x.XSendEvent.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_long, ctypes.c_void_p]
            x.XFlush.argtypes = [ctypes.c_void_p]
            display = x.XOpenDisplay(None)
            window = int(sys.argv[1])
            class Message(ctypes.Structure):
                _fields_ = [('type', ctypes.c_int), ('serial', ctypes.c_ulong), ('send_event', ctypes.c_int), ('display', ctypes.c_void_p),
                            ('window', ctypes.c_ulong), ('message_type', ctypes.c_ulong), ('format', ctypes.c_int), ('data', ctypes.c_long * 5),
                            ('pad', ctypes.c_long * 12)]
            message = Message(type=33, send_event=1, display=display, window=window, message_type=x.XInternAtom(display, b'WM_PROTOCOLS', 0), format=32)
            message.data[0] = x.XInternAtom(display, b'WM_DELETE_WINDOW', 0)
            x.XSendEvent(display, window, 0, 0, ctypes.byref(message))
            x.XFlush(display)
            print('sent', flush=True)
            """;
        var script = Path.Combine(desktop.Session.Home, "close.py");
        await File.WriteAllTextAsync(script, Code, cancel);
        var sent = await desktop.Session.RunAsync("python3", [script, window], cancel: cancel);
        Assert.Equal("sent", sent.Out.Trim());
    }

    [GeneratedRegex("\"taskbar\"\\s*:\\s*true", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Taskbar();

    [GeneratedRegex("0x[0-9a-f]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Window();
}
