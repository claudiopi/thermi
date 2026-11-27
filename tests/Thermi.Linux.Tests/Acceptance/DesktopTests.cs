using System.Diagnostics;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// What thermi does with the rest of the Linux desktop (<c>docs/specs/006-linux/spec.md</c>, R18 and R20 to R22): its
/// settings window, its tray icons, its hotkeys and its notices.
/// </summary>
public sealed class DesktopTests
{
    private static readonly TestMonitor Monitor = new(1920, 1080);

    // The overlay's place on screen, below every bar, as a region of a screenshot.
    private static readonly (int X, int Y, int Width, int Height) OnWindow = (Scene.OnWindow.X, Scene.OnWindow.Y, 200, 40);

    // A temperature past every line the recorded CPU reads, and one below it, so an edit changes the item's level.
    private const string Calm = """{"kind":"threshold","item":"cpu.temperature","warning":110,"critical":120}""";
    private const string Hot = """{"kind":"threshold","item":"cpu.temperature","warning":1,"critical":2}""";

    // Holds a combination as another program does, through the Shell's own grab, which its unsafe mode lets in, for as long
    // as it runs; it says so once the Shell took it.
    private const string Holder = """
        import sys, gi
        from gi.repository import Gio, GLib
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        action = bus.call_sync('org.gnome.Shell', '/org/gnome/Shell', 'org.gnome.Shell', 'GrabAccelerator',
                               GLib.Variant('(suu)', (sys.argv[1], 1, 0)), None, Gio.DBusCallFlags.NONE, -1, None).unpack()[0]
        print('held' if action else 'refused', flush=True)
        GLib.MainLoop().run()
        """;

    public enum Tray
    {
        Gnome,
        StatusNotifier,
    }

    public enum Hotkeys
    {
        Extension,
        Portal,
        X11,
        Held,
    }

    [Fact(Timeout = Bound.Test)]
    public async Task TheSettingsWindowWorksOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        var session = desktop.Session;
        var config = ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] };
        await desktop.WriteSettingsAsync(config, cancel);
        await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var item = config.Overlays[0].Items[0].Id;

        // Every way a person opens it, each with the window already open: one window still shows, on the item double-clicked.
        // Behind another program's window, the overlay's menu and a double-click bring it in front, as GNOME lets a program
        // started on a person's click do.
        var rows = Menus.OfOverlay(config, 0, new MenuState(false, false, ServiceState.Running, null, [], false), item);
        async Task Menu()
        {
            await desktop.Input.ClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel, button: 3);
            await Accessible.ChooseAsync(desktop, rows.ToList().FindIndex(row => row.Entry == MenuEntry.Settings), cancel);
        }

        await OpenedAsync(thermi, Menu, cancel);
        await InFrontAsync(desktop, cancel);
        var onItem = await OpenedAsync(thermi, () => desktop.Input.DoubleClickAsync(Scene.OnWindow.X + 8, Scene.OnWindow.Y + 8, cancel), cancel);
        await Accessible.ActiveAsync(session, "thermi", cancel);
        await InFrontAsync(desktop, cancel);
        await OpenedAsync(thermi, Menu, cancel);
        await Accessible.ActiveAsync(session, "thermi", cancel);
        await OpenedAsync(thermi, () => session.RunAsync(Built.Program("thermi"), ["settings"], cancel: cancel), cancel);
        var launcher = Path.Combine(Built.Root, "src", "Thermi.Linux", "Packaging", "io.github.claudiopi.thermi.desktop");
        await OpenedAsync(thermi, () => session.RunAsync("gio", ["launch", launcher], cancel: cancel), cancel);
        var window = Assert.Single(await Accessible.WindowsAsync(session, cancel), shown => shown.Program == "thermi");

        // A slider's change shows on the overlay within 300 ms of the slider moving, and undo takes it back.
        var shown = await desktop.ScreenshotAsync(cancel);
        var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        var slid = await Accessible.SlideAsync(desktop, 40, cancel);
        var took = (await applied).At - slid;
        var undone = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await desktop.Input.PressAsync("Control_L+z", cancel);
        await undone;
        var back = await desktop.ScreenshotAsync(cancel);
        // Closed as the desktop closes a window: Esc belongs to the page, which closes its menus and dialogs with it. WebKit's
        // own processes, the page's and the network's, are the window's too, and go with it.
        var processes = ProcessTree.Under(window.Process).Prepend(window.Process).Select(Process.GetProcessById).ToList();
        await desktop.Input.PressAsync("Alt_L+F4", cancel);
        foreach (var process in processes)
        {
            using (process)
            {
                await process.WaitForExitAsync(cancel);
            }
        }

        Assert.Equal(item, onItem.About);
        Assert.Equal(1, window.Windows);
        Assert.InRange(took, 0, 0.3);
        Assert.Equal(0, await Pictures.ChangedAsync(session, shown, back, OnWindow, cancel));
        Assert.DoesNotContain(await Accessible.WindowsAsync(session, cancel), program => program.Process == window.Process);
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Tray.Gnome, Skip = "Waiting for its code")]
    [InlineData(Tray.StatusNotifier, Skip = "Waiting for its code")]
    public async Task TrayIconsShowOnEveryDesktop(Tray row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(row == Tray.Gnome ? DesktopKind.Gnome : DesktopKind.X11, [Monitor], cancel);
        var session = desktop.Session;
        var tray = row == Tray.StatusNotifier ? await TrayWatcher.ServeAsync(session, cancel) : null;
        var items = Scene.Overlay.Items.Select((item, index) => index < 2 ? item with { Tray = true } : item).ToList();
        var config = ThermiConfig.New with { Overlays = [Scene.Overlay with { Items = items }] };
        await desktop.WriteSettingsAsync(config, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);
        Assert.Equal(0, (await session.RunAsync("thermi-cli", ["edit", Calm], cancel: cancel)).Exit);
        var before = await TopBarAsync(desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);

        var calm = await IconsAsync(desktop, thermi, tray, null, cancel);
        var hot = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        Assert.Equal(0, (await session.RunAsync("thermi-cli", ["edit", Hot], cancel: cancel)).Exit);
        await hot;
        var heated = await IconsAsync(desktop, thermi, tray, [.. calm.Icons.Select(icon => icon.At)], cancel);
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        var restarted = await desktop.ShowThermiAsync([], cancel);
        var back = await IconsAsync(desktop, restarted, tray, null, cancel);

        if (tray is null)
        {
            Assert.NotEqual(0, await Pictures.ChangedAsync(session, before, calm.Bar, (0, 0, 400, TestPanel.Height), cancel));
            Assert.NotEqual(0, await Pictures.ChangedAsync(session, calm.Bar, heated.Bar, (0, 0, 400, TestPanel.Height), cancel));
            Assert.Equal(0, await Pictures.ChangedAsync(session, heated.Bar, back.Bar, (0, 0, 400, TestPanel.Height), cancel));
            return;
        }

        Assert.Equal(3, calm.Icons.Count);
        Assert.Equal(calm.Icons.Select(icon => icon.Id), back.Icons.Select(icon => icon.Id));
        Assert.NotEqual(calm.Icons.Select(icon => icon.Shows), heated.Icons.Select(icon => icon.Shows));
        Assert.Equal(heated.Icons.Select(icon => icon.Shows), back.Icons.Select(icon => icon.Shows));
        // Each icon shows what thermi paints for it, its value in its level's color.
        foreach (var icon in back.Icons)
        {
            Assert.Equal(0, await PaintedAsync(desktop, feed.Snapshot, icon, cancel));
        }

        // thermi's icon has thermi's menu, and each item's icon its overlay's, opened from the tray on that item.
        var state = new MenuState(false, false, ServiceState.Running, null, [], false);
        var settled = await Settings.ReadAsync(session, cancel);
        var ofItems = items.Where(item => item.Tray).Select(item => Menus.OfOverlay(settled, 0, state, item.Id, fromTray: true).Count);
        int[] expected = [Menus.Thermis(settled, state).Count, .. ofItems];
        var entries = await Task.WhenAll(back.Icons.Select(icon => tray.MenuEntriesAsync(icon.At, cancel)));
        Assert.Equal(expected.Order(), entries.Order());
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Hotkeys.Extension, Skip = "Waiting for its code")]
    [InlineData(Hotkeys.Portal, Skip = "Waiting for its code")]
    [InlineData(Hotkeys.X11, Skip = "Waiting for its code")]
    [InlineData(Hotkeys.Held, Skip = "Waiting for its code")]
    public async Task HotkeysWorkOnEveryDesktop(Hotkeys row)
    {
        var cancel = TestContext.Current.CancellationToken;
        var kind = row switch
        {
            Hotkeys.Extension or Hotkeys.Held => DesktopKind.Gnome,
            Hotkeys.Portal => DesktopKind.KWin,
            Hotkeys.X11 => DesktopKind.X11,
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown case."),
        };
        await using var desktop = await TestDesktop.StartAsync(kind, [Monitor], cancel);
        var hotkeys = new HotkeysConfig
        {
            Toggle = row == Hotkeys.Held ? "Ctrl+Alt+T" : "Ctrl+Alt+F9",
            NextStyle = "Ctrl+Alt+F10",
            ClickThrough = "Ctrl+Alt+F11",
            ResetMinMax = "Ctrl+Alt+F12",
        };
        // The CPU's temperature shows its highest, which only Reset min/max takes back to now.
        var items = Scene.Overlay.Items.Select(item => item.Metric == Metrics.CpuTemperature.Id ? item with { Stat = ItemStat.Max } : item).ToList();
        var overlay = Scene.Overlay with { Position = Scene.OnWindow, Items = items };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay], Hotkeys = hotkeys }, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        if (row == Hotkeys.Held)
        {
            await HeldIsRefusedAsync(desktop, cancel);
            return;
        }

        var asks = row == Hotkeys.Portal ? await PortalDialog.AsksAsync(desktop.Session, cancel) : null;
        var thermi = await desktop.ShowThermiAsync([], cancel);
        if (asks is not null)
        {
            // The portal asks the person once; its dialog's default button allows it, once the dialog is in front.
            await asks;
            await desktop.Input.PressAsync("Return", cancel);
        }

        var before = await Settings.ReadAsync(desktop.Session, cancel);
        var styled = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel, count: 2);
        await desktop.Input.PressAsync("Control_L+Alt_L+F10", cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+F11", cancel);
        await styled;
        var after = await Settings.ReadAsync(desktop.Session, cancel);
        var reset = await ResetsAsync(desktop, thermi, feed, cancel);
        var shown = await desktop.ScreenshotAsync(cancel);
        var hiding = Traced.NextAsync(thermi.Error, Traced.Hidden, cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+F9", cancel);
        await hiding;
        var hidden = await desktop.ScreenshotAsync(cancel);

        Assert.NotEqual(before.Overlays[0].Style, after.Overlays[0].Style);
        Assert.True(after.Overlays[0].ClickThrough);
        Assert.NotEqual(0, reset);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, shown, hidden, OnWindow, cancel));
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task NoticesAreTheDesktopsNotifications()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], extension: null, cancel);
        var session = desktop.Session;
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        var items = Scene.Overlay.Items.Select(item => item.Metric == Metrics.CpuTemperature.Id ? item with { Notify = true } : item).ToList();
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Items = items }] }, cancel);
        await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);
        Assert.Equal(0, (await session.RunAsync("thermi-cli", ["edit", Hot], cancel: cancel)).Exit);
        var firstRun = notices.NextAsync("first-run", cancel);
        var missing = notices.NextAsync("install-extension", cancel);
        var critical = notices.NextAsync("critical", cancel);
        await OffTheNetworkAsync(session, cancel);
        var thermi = await desktop.StartThermiAsync(["--trace"], cancel);

        // The item is critical from thermi's first second on, and its notice comes once it stayed so for 10 s.
        var first = await Traced.FirstAsync(thermi.Error, Traced.Paint, cancel);
        var hot = await critical;
        // The first start's notice opens the window, as its click does on Windows; the extension's asks the Shell again.
        var opened = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        await notices.ClickAsync(await firstRun, "first-run", cancel);
        await opened;
        var asked = notices.NextInstallAsync(cancel);
        await notices.ClickAsync(await missing, "install-extension", cancel);
        await asked;
        // Once: after as many more of thermi's seconds as the critical notice waited, none of them came again.
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: (int)NotifyMe.After.TotalSeconds + 1);
        await notices.SyncAsync(cancel);

        Assert.All(["first-run", "install-extension", "critical"], action => Assert.Equal(1, notices.Count(action)));
        Assert.Equal("thermi", hot.App);
        Assert.True(hot.At - first.At > NotifyMe.After.TotalSeconds - 1, $"The critical notice came {hot.At - first.At} s after the first second.");
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

    // Another program's window opens over thermi's and takes the keys, as a person's next window does.
    private static async Task InFrontAsync(TestDesktop desktop, CancellationToken cancel)
    {
        _ = await TestWindow.OpenAsync(desktop.Session, fullScreen: false, cancel);
        await Accessible.ActiveAsync(desktop.Session, TestWindow.Title, cancel);
    }

    // Opens the settings window as <paramref name="open"/> does, once the window says it showed.
    private static async Task<Traced> OpenedAsync(SessionProcess thermi, Func<Task> open, CancellationToken cancel)
    {
        var shown = Traced.NextAsync(thermi.Error, Traced.WindowShown, cancel);
        await open();
        return await shown;
    }

    // The PC cools: the highest stays as it was, until Reset min/max's hotkey takes it to now. Says how much of the overlay
    // the reset changed, two of thermi's seconds after the press.
    private static async Task<long> ResetsAsync(TestDesktop desktop, SessionProcess thermi, SensorFeed.Steady feed, CancellationToken cancel)
    {
        feed.Service.Kill();
        await feed.Service.WaitForExitAsync(cancel);
        var cooled = Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: 2);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel, Cool);
        await cooled;
        var highest = await desktop.ScreenshotAsync(cancel);
        var reset = Traced.NextAsync(thermi.Error, Traced.Paint, cancel, count: 2);
        await desktop.Input.PressAsync("Control_L+Alt_L+F12", cancel);
        await reset;
        return await Pictures.ChangedAsync(desktop.Session, highest, await desktop.ScreenshotAsync(cancel), OnWindow, cancel);
    }

    // The CPU's own chip reads 30 °C, below anything the recording read.
    private static void Cool(string second)
    {
        var chips = Directory.EnumerateFiles(second, "name", SearchOption.AllDirectories).Where(name => File.ReadAllText(name).Trim() == "k10temp");
        foreach (var input in chips.SelectMany(name => Directory.EnumerateFiles(Path.GetDirectoryName(name)!, "temp*_input")))
        {
            File.WriteAllText(input, "30000\n");
        }
    }

    // Another program holds Ctrl+Alt+T before thermi starts: thermi says it cannot take it, and its press does nothing of
    // thermi's, while thermi's other hotkeys work.
    private static async Task HeldIsRefusedAsync(TestDesktop desktop, CancellationToken cancel)
    {
        var script = Path.Combine(desktop.Session.Home, "holder.py");
        await File.WriteAllTextAsync(script, Holder, cancel);
        var holder = await desktop.Session.StartAsync("python3", [script, "<Control><Alt>t"], cancel: cancel);
        Assert.Equal("held", await holder.ReadyAsync(cancel));
        var thermi = await desktop.StartThermiAsync(["--trace"], cancel);
        var refused = await Traced.FirstAsync(thermi.Error, Traced.HotkeyRefused, cancel);
        await Traced.FirstAsync(thermi.Error, Traced.Shown, cancel);

        var styled = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        var hidden = thermi.Error.NextAsync(line => Traced.Of(line)?.Event == Traced.Hidden, cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+t", cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+F10", cancel);
        await styled;

        Assert.Equal("toggle", refused.About);
        // thermi writes what each press did in turn: the held one's would have come before the next one's.
        Assert.False(hidden.IsCompleted);
    }

    // The top bar's right end, where GNOME shows the icons, as a picture of its own.
    private static async Task<string> TopBarAsync(TestDesktop desktop, CancellationToken cancel)
    {
        if (desktop.Kind != DesktopKind.Gnome)
        {
            return "";
        }

        var screen = await desktop.ScreenshotAsync(cancel);
        var bar = Path.Combine(desktop.Session.Home, $"bar-{Guid.NewGuid():N}.png");
        var crop = $"400x{TestPanel.Height}+{Monitor.Width - 400}+0";
        var cut = await desktop.Session.RunAsync("convert", [screen, "-crop", crop, "+repage", bar], cancel: cancel);
        return cut.Exit == 0 ? bar : throw new InvalidOperationException($"No top bar: {cut.Error}");
    }

    // What the icons show: on GNOME, the top bar's right end; in a tray, each icon by its id, with its picture. A tray reads
    // the three icons thermi registers as it starts, or, while it runs, the icons it read before.
    // Each read follows the next of thermi's seconds, once it painted what the icons show.
    private static async Task<Shown> IconsAsync(
        TestDesktop desktop, SessionProcess thermi, TrayWatcher? tray, IReadOnlyList<(string Name, string Path)>? known, CancellationToken cancel)
    {
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        if (tray is null)
        {
            return new Shown(await TopBarAsync(desktop, cancel), []);
        }

        var at = known ?? [await tray.NextAsync(cancel), await tray.NextAsync(cancel), await tray.NextAsync(cancel)];
        List<Icon> icons = [];
        foreach (var item in at)
        {
            // gdbus prints a string as (<'cpu.temperature'>,).
            var id = (await tray.PropertyAsync(item, "Id", cancel)).Trim('(', ')', '<', '>', ',', '\'');
            icons.Add(new Icon(item, id, await tray.PropertyAsync(item, "IconPixmap", cancel), await tray.PictureAsync(item, cancel)));
        }

        return new Shown("", [.. icons.OrderBy(icon => icon.Id, StringComparer.Ordinal)]);
    }

    // How far an icon's picture is from the one thermi preview paints for the icon of its id, with the settings as they are.
    private static async Task<long> PaintedAsync(TestDesktop desktop, string snapshot, Icon icon, CancellationToken cancel)
    {
        var session = desktop.Session;
        var painted = Path.Combine(session.Home, $"painted-{Guid.NewGuid():N}.png");
        var preview = await session.RunAsync(
            Built.Program("thermi"), ["preview", snapshot, painted, "1", Settings.PathOf(session), "--icon", icon.Id], cancel: cancel);
        Assert.True(preview.Exit == 0, preview.Error);
        var (width, height) = await Pictures.SizeAsync(session, icon.Picture, cancel);
        Assert.Equal((width, height), await Pictures.SizeAsync(session, painted, cancel));
        return await Pictures.ChangedAsync(session, icon.Picture, painted, (0, 0, width, height), cancel);
    }

    private sealed record Icon((string Name, string Path) At, string Id, string Shows, string Picture);

    private sealed record Shown(string Bar, IReadOnlyList<Icon> Icons);
}
