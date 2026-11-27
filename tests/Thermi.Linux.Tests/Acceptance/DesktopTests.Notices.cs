using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;
using Thermi.Linux.Tray;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>A20: thermi's notices as the desktop's own notifications.</summary>
public sealed partial class DesktopTests
{
    private const string Notify = """{"kind":"item","item":"cpu.temperature","field":"notify","value":true}""";

    // Holds the window's name on the session's bus as a program that is no window would, so the window thermi starts
    // ends at once with an error.
    private const string Squatter = """
        import sys
        from gi.repository import Gio, GLib
        Gio.bus_own_name(Gio.BusType.SESSION, sys.argv[1], Gio.BusNameOwnerFlags.DO_NOT_QUEUE,
                         None, lambda *_: print('held', flush=True), lambda *_: print('taken', flush=True))
        GLib.MainLoop().run()
        """;

    public enum NoticeCase
    {
        Gnome,
        X11,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(NoticeCase.Gnome)]
    [InlineData(NoticeCase.X11)]
    public Task NoticesAreTheDesktopsNotifications(NoticeCase row) =>
        row == NoticeCase.X11 ? NoticesOffGnomeAsync(TestContext.Current.CancellationToken) : NoticesOnGnomeAsync(TestContext.Current.CancellationToken);

    // On GNOME, the Shell's: thermi's first start, a critical item and the missing extension.
    private static async Task NoticesOnGnomeAsync(CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], extension: null, cancel);
        var session = desktop.Session;
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);
        var firstRun = notices.NextAsync("first-run", cancel);
        var missing = notices.NextAsync("install-extension", cancel);
        var critical = notices.NextAsync("critical", cancel);
        await OffTheNetworkAsync(session, cancel);
        // No settings yet: thermi's first start, which writes them.
        var thermi = await desktop.StartThermiAsync(["--trace"], cancel);

        // The CPU's temperature is critical with Notify me from thermi's first second on, and its notice comes once it stayed
        // so for 10 s.
        var first = await Traced.FirstAsync(thermi.Error, Traced.Paint, cancel);
        await EditAsync(session, Notify, cancel);
        await EditAsync(session, Hot, cancel);
        var hot = await critical;
        // The first start's notice opens the window, as its click does on Windows; a window that dies says so, and its
        // notice opens it again. The extension's asks the Shell again.
        var squatter = await SquatAsync(session, cancel);
        var failed = notices.NextAsync(ThermiNotices.OpenAgain, cancel);
        await notices.ClickAsync(await firstRun, "first-run", cancel);
        var told = await failed;
        squatter.Kill();
        await squatter.WaitForExitAsync(cancel);
        var opened = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        await notices.ClickAsync(told, ThermiNotices.OpenAgain, cancel);
        await opened;
        var asked = notices.NextInstallAsync(cancel);
        await notices.ClickAsync(await missing, "install-extension", cancel);
        await asked;
        // Once: after as many more of thermi's seconds as the critical notice waited, none of them came again.
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: (int)NotifyMe.After.TotalSeconds + 1);
        await notices.SyncAsync(cancel);

        Assert.All(["first-run", "install-extension", "critical", ThermiNotices.OpenAgain], action => Assert.Equal(1, notices.Count(action)));
        Assert.Equal("thermi", hot.App);
        Assert.True(hot.At - first.At > NotifyMe.After.TotalSeconds - 1, $"The critical notice came {hot.At - first.At} s after the first second.");
    }

    // Off GNOME, a server of the test's own: the hotkeys hide the overlays and turn click-through on, each told as a notice, and
    // a button of the first and a click on the second itself do what they do on Windows.
    private static async Task NoticesOffGnomeAsync(CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.X11, [Monitor], cancel);
        var hotkeys = new HotkeysConfig { Toggle = "Ctrl+Shift+J", ClickThrough = "Ctrl+Shift+L" };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }], Hotkeys = hotkeys }, cancel);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);

        var hidden = notices.NextAsync(ThermiNotices.ShowIt, cancel);
        await desktop.Input.PressAsync("Control_L+Shift_L+j", cancel);
        var shown = Traced.NextAsync(thermi.Error, Traced.Shown, cancel);
        await notices.ClickAsync(await hidden, ThermiNotices.ShowIt, cancel);
        await shown;
        var through = notices.NextAsync(ThermiNotices.Undo, cancel);
        var turnedOn = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await desktop.Input.PressAsync("Control_L+Shift_L+l", cancel);
        await turnedOn;
        var undone = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await notices.ClickAsync(await through, ThermiNotices.Body, cancel);
        await undone;

        Assert.False((await Settings.ReadAsync(desktop.Session, cancel)).Overlays[0].ClickThrough);
    }

    private static async Task<SessionProcess> SquatAsync(OwnSession session, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "squatter.py");
        await File.WriteAllTextAsync(script, Squatter, cancel);
        var squatter = await session.StartAsync("python3", [script, WindowProcess.Id], cancel: cancel);
        Assert.Equal("held", await squatter.ReadyAsync(cancel));
        return squatter;
    }

    // The Shell asks extensions.gnome.org for the extension thermi asks it to install: the session's proxy, which every
    // connection of the Shell's follows, refuses them all, so the test never reaches the network.
    private static async Task OffTheNetworkAsync(OwnSession session, CancellationToken cancel)
    {
        string[][] settings =
        [
            ["org.gnome.system.proxy.http", "host", "127.0.0.1"], ["org.gnome.system.proxy.http", "port", "9"],
            ["org.gnome.system.proxy.https", "host", "127.0.0.1"], ["org.gnome.system.proxy.https", "port", "9"],
            ["org.gnome.system.proxy", "mode", "manual"],
        ];
        foreach (var setting in settings)
        {
            Assert.Equal(0, (await session.RunAsync("gsettings", ["set", .. setting], cancel: cancel)).Exit);
        }
    }
}
