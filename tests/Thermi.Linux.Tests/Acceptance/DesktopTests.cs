using System.Diagnostics;
using System.Globalization;
using Thermi.App.SettingsFile;
using Thermi.Core;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// What thermi does with the rest of the Linux desktop (<c>docs/specs/006-linux/spec.md</c>, R18 and R20 to R22): its
/// settings window, its tray icons, its hotkeys and its notices.
/// </summary>
public sealed partial class DesktopTests
{
    /// <summary>The tests' own program, started in a session with it, asks the overlay for its hotkeys (<see cref="AskHotkeyStatus"/>).</summary>
    public const string HotkeyStatus = "--hotkey-status";

    private static readonly TestMonitor Monitor = new(1920, 1080);

    // The overlay's place on screen, below every bar, as a region of a screenshot.
    private static readonly (int X, int Y, int Width, int Height) OnWindow = (Scene.OnWindow.X, Scene.OnWindow.Y, 200, 40);

    // A temperature past every line the recorded CPU reads, and one below it, so an edit changes the item's level.
    private const string Calm = """{"kind":"threshold","item":"cpu.temperature","warning":98,"critical":99}""";
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

        // A slider's change shows on the overlay within 300 ms of the slider moving, and undo takes it back. The change timed is
        // warm, as a steady PC's are: the first of a window's process, undone untimed, decodes its first call, lays out the
        // page's first edit and, under --trace alone, writes its first debug line, all cold, which takes past 300 ms on a busy
        // runner. The cold one is timed by hand, in docs/RELEASES.md's "The window's speed on Linux".
        var shown = await desktop.ScreenshotAsync(cancel);
        var first = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await Accessible.SlideAsync(desktop, 40, cancel);
        await first;
        var reverted = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await desktop.Input.PressAsync("Control_L+z", cancel);
        await reverted;
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
        // The window writes on thermi's standard error: GTK warned of nothing its edits did, as of a theme set again each.
        Assert.DoesNotContain(thermi.Error.Text.Split('\n'), line => line.Contains("Gtk-WARNING", StringComparison.Ordinal));
        Assert.Equal(0, await Pictures.ChangedAsync(session, shown, back, OnWindow, cancel));
        Assert.DoesNotContain(await Accessible.WindowsAsync(session, cancel), program => program.Process == window.Process);
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Hotkeys.Extension)]
    [InlineData(Hotkeys.Portal)]
    [InlineData(Hotkeys.X11)]
    [InlineData(Hotkeys.Held)]
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
        // Letters, as xdotool reaches an F key through a level of X11's keymap that holds Alt too, and Ctrl+Alt with an F
        // key switches to another console, which every desktop holds for itself.
        var hotkeys = new HotkeysConfig
        {
            Toggle = row == Hotkeys.Held ? "Ctrl+Alt+T" : "Ctrl+Shift+J",
            NextStyle = "Ctrl+Shift+K",
            ClickThrough = "Ctrl+Shift+L",
            ResetMinMax = "Ctrl+Shift+U",
        };
        // The CPU's temperature shows its highest, which only Reset min/max takes back to now.
        var items = Scene.Overlay.Items.Select(item => item.Metric == Metrics.CpuTemperature.Id ? item with { Stat = ItemStat.Max } : item).ToList();
        var overlay = Scene.Overlay with { Position = Scene.OnWindow, Items = items };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay], Hotkeys = hotkeys }, cancel);
        // A look saved beside the settings, which Next look wears.
        var looks = new SavedStyles(ConfigStore.StylesFolder(Settings.PathOf(desktop.Session)));
        Assert.Null(looks.Save(overlay.Style with { Font = StyleFont.Consolas }, "Mono"));
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
        await desktop.Input.PressAsync("Control_L+Shift_L+k", cancel);
        await desktop.Input.PressAsync("Control_L+Shift_L+l", cancel);
        await styled;
        var after = await Settings.ReadAsync(desktop.Session, cancel);
        var reset = await ResetsAsync(desktop, thermi, feed, cancel);
        var shown = await desktop.ScreenshotAsync(cancel);
        var hiding = Traced.NextAsync(thermi.Error, Traced.Hidden, cancel);
        await desktop.Input.PressAsync("Control_L+Shift_L+j", cancel);
        await hiding;
        var hidden = await desktop.ScreenshotAsync(cancel);

        Assert.NotEqual(before.Overlays[0].Style, after.Overlays[0].Style);
        Assert.True(after.Overlays[0].ClickThrough);
        Assert.NotEqual(0, reset);
        Assert.NotEqual(0, await Pictures.ChangedAsync(desktop.Session, shown, hidden, OnWindow, cancel));
    }

    // An edit as an agent makes it, which thermi-cli says it wrote.
    private static async Task EditAsync(OwnSession session, string edit, CancellationToken cancel)
    {
        var edited = await session.RunAsync("thermi-cli", ["edit", edit], cancel: cancel);
        Assert.True(edited.Exit == 0, $"thermi-cli edit {edit}: {edited.Out}{edited.Error}");
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
        await desktop.Input.PressAsync("Control_L+Shift_L+u", cancel);
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
        // The settings window asks the running overlay, as Windows' does, and says the hotkey is held.
        var asked = await desktop.Session.RunAsync(Built.Program("Thermi.Linux.Tests"), [HotkeyStatus], cancel: cancel);
        var hotkeys = (await Settings.ReadAsync(desktop.Session, cancel)).Hotkeys;

        var styled = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        var hidden = thermi.Error.NextAsync(line => Traced.Of(line)?.Event == Traced.Hidden, cancel);
        await desktop.Input.PressAsync("Control_L+Alt_L+t", cancel);
        await desktop.Input.PressAsync("Control_L+Shift_L+k", cancel);
        await styled;

        Assert.Equal("toggle", refused.About);
        Assert.Equal(HotkeyFailures.Toggle, Core.Hotkeys.Trusted(long.Parse(asked.Out.Trim(), CultureInfo.InvariantCulture), hotkeys));
        // thermi writes what each press did in turn: the held one's would have come before the next one's.
        Assert.False(hidden.IsCompleted);
    }

    /// <summary>
    /// The tests' own program as the held row starts it: the settings window's link to the overlay asks it for its hotkeys,
    /// and writes the answer, or "none".
    /// </summary>
    public static int AskHotkeyStatus()
    {
        Gio.Module.Initialize();
        using var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        using var overlay = new OverlayLink(bus, () => { });
        Console.WriteLine(overlay.HotkeyStatus()?.ToString(CultureInfo.InvariantCulture) ?? "none");
        return 0;
    }
}
