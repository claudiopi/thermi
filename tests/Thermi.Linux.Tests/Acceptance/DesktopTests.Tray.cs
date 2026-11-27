using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Agents;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>A18: thermi's tray icons, in GNOME's top bar and in a tray of StatusNotifierItems.</summary>
public sealed partial class DesktopTests
{
    [Theory(Timeout = Bound.Test)]
    [InlineData(Tray.Gnome)]
    [InlineData(Tray.StatusNotifier)]
    public async Task TrayIconsShowOnEveryDesktop(Tray row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(row == Tray.Gnome ? DesktopKind.Gnome : DesktopKind.X11, [Monitor], cancel);
        var session = desktop.Session;
        var tray = row == Tray.StatusNotifier ? await TrayWatcher.ServeAsync(session, cancel) : null;
        var items = Scene.Overlay.Items.Select((item, index) => index < 2 ? item with { Tray = true } : item).ToList();
        // The overlay on a window, so only the icons change the top bar.
        var config = ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow, Items = items }] };
        await desktop.WriteSettingsAsync(config, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);
        await EditAsync(session, Calm, cancel);
        var (screen, bar) = await TopBarAsync(desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);

        var calm = await IconsAsync(desktop, thermi, tray, null, cancel);
        var hot = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        await EditAsync(session, Hot, cancel);
        await hot;
        var heated = await IconsAsync(desktop, thermi, tray, [.. calm.Icons.Select(icon => icon.At)], cancel);
        thermi.Kill();
        await thermi.WaitForExitAsync(cancel);
        var restarted = await desktop.ShowThermiAsync([], cancel);
        var back = await IconsAsync(desktop, restarted, tray, null, cancel);
        // thermi's icon has thermi's menu, and each item's icon its overlay's, opened from the tray on that item; no menu of
        // Linux's offers a copy of the overlay, which reaches no clipboard.
        var state = new MenuState(false, false, ServiceState.Running, null, [], false) { CanCopy = false };
        var settled = await Settings.ReadAsync(session, cancel);
        var thermis = Menus.Thermis(settled, state).Count;

        if (tray is null)
        {
            Assert.NotEqual(0, await Pictures.ChangedAsync(session, bar, calm.Bar, (0, 0, 400, TestPanel.Height), cancel));
            Assert.NotEqual(0, await Pictures.ChangedAsync(session, calm.Bar, heated.Bar, (0, 0, 400, TestPanel.Height), cancel));
            Assert.Equal(0, await Pictures.ChangedAsync(session, heated.Bar, back.Bar, (0, 0, 400, TestPanel.Height), cancel));
            // thermi's icon shows what thermi paints for it over the bar, and a press of it opens thermi's menu, the Shell's own.
            var (at, centre) = await ShellIconAsync(desktop, TrayScene.ThermisId, cancel);
            var painted = await PreviewAsync(session, feed.Snapshot, TrayScene.ThermisId, cancel);
            Assert.Equal(0, await Pictures.DifferingAsync(session, screen, back.Screen, painted, at.X, at.Y, cancel));
            await desktop.Input.ClickAsync(centre.X, centre.Y, cancel);
            Assert.Equal(("gnome-shell", thermis), Assert.Single(await Accessible.OpenMenusAsync(session, cancel)));
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

        var ofItems = items.Where(item => item.Tray).Select(item => Menus.OfOverlay(settled, 0, state, item.Id, fromTray: true).Count);
        int[] expected = [thermis, .. ofItems];
        var entries = await Task.WhenAll(back.Icons.Select(icon => tray.MenuEntriesAsync(icon.At, cancel)));
        Assert.Equal(expected.Order(), entries.Order());
    }

    // The screen and the top bar's right end, where GNOME shows the icons, as a picture of its own.
    private static async Task<(string Screen, string Bar)> TopBarAsync(TestDesktop desktop, CancellationToken cancel)
    {
        if (desktop.Kind != DesktopKind.Gnome)
        {
            return ("", "");
        }

        var screen = await desktop.ScreenshotAsync(cancel);
        var bar = Path.Combine(desktop.Session.Home, $"bar-{Guid.NewGuid():N}.png");
        var crop = $"400x{TestPanel.Height}+{Monitor.Width - 400}+0";
        var cut = await desktop.Session.RunAsync("convert", [screen, "-crop", crop, "+repage", bar], cancel: cancel);
        return cut.Exit == 0 ? (screen, bar) : throw new InvalidOperationException($"No top bar: {cut.Error}");
    }

    // Where the Shell shows the icon of a key: its picture's top left, and its button's centre, which a press takes.
    private static async Task<((int X, int Y) At, (int X, int Y) Centre)> ShellIconAsync(TestDesktop desktop, string key, CancellationToken cancel)
    {
        var script = $"const b = Main.panel.statusArea['thermi-{key}']; const p = b.get_first_child();"
            + " [p.get_transformed_position(), b.get_transformed_position(), b.get_transformed_size()].flat().map(Math.round).join(' ')";
        var said = await desktop.Session.RunAsync("gdbus", TestDesktop.Call("org.gnome.Shell", "/org/gnome/Shell", "Eval", script), cancel: cancel);
        // gdbus prints the Shell's answer as (true, '"<x> <y> ..."').
        var parts = Regex.Matches(said.Out, @"-?\d+", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(number => (int)(int.Parse(number.Value, CultureInfo.InvariantCulture) * desktop.Scale)).ToArray();
        Assert.True(parts.Length == 6 && said.Out.StartsWith("(true", StringComparison.Ordinal), $"The Shell shows no icon {key}: {said.Out}{said.Error}");
        return ((parts[0], parts[1]), (parts[2] + (parts[4] / 2), parts[3] + (parts[5] / 2)));
    }

    // What the icons show: on GNOME, the screen and the top bar's right end; in a tray, each icon by its id, with its picture.
    // A tray reads the three icons thermi registers as it starts, or, while it runs, the icons it read before.
    // Each read follows the next of thermi's seconds, once it painted what the icons show.
    private static async Task<Shown> IconsAsync(
        TestDesktop desktop, SessionProcess thermi, TrayWatcher? tray, IReadOnlyList<(string Name, string Path)>? known, CancellationToken cancel)
    {
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        if (tray is null)
        {
            var (screen, bar) = await TopBarAsync(desktop, cancel);
            return new Shown(screen, bar, []);
        }

        var at = known ?? [await tray.NextAsync(cancel), await tray.NextAsync(cancel), await tray.NextAsync(cancel)];
        List<Icon> icons = [];
        foreach (var item in at)
        {
            // gdbus prints a string as (<'item:cpu.temperature'>,).
            var id = (await tray.PropertyAsync(item, "Id", cancel)).Trim('(', ')', '<', '>', ',', '\'');
            icons.Add(new Icon(item, id, await tray.PropertyAsync(item, "IconPixmap", cancel), await tray.PictureAsync(item, cancel)));
        }

        return new Shown("", "", [.. icons.OrderBy(icon => icon.Id, StringComparer.Ordinal)]);
    }

    // How far an icon's picture is from the one thermi preview paints for the icon of its key, with the settings as they are.
    private static async Task<long> PaintedAsync(TestDesktop desktop, string snapshot, Icon icon, CancellationToken cancel)
    {
        var session = desktop.Session;
        var item = icon.Id.StartsWith(AgentReads.ItemPrefix, StringComparison.Ordinal) ? icon.Id[AgentReads.ItemPrefix.Length..] : icon.Id;
        var painted = await PreviewAsync(session, snapshot, item, cancel);
        var (width, height) = await Pictures.SizeAsync(session, icon.Picture, cancel);
        Assert.Equal((width, height), await Pictures.SizeAsync(session, painted, cancel));
        return await Pictures.ChangedAsync(session, icon.Picture, painted, (0, 0, width, height), cancel);
    }

    // The icon thermi preview paints for an item's id, or thermi's own, with the settings as they are.
    private static async Task<string> PreviewAsync(OwnSession session, string snapshot, string icon, CancellationToken cancel)
    {
        var painted = Path.Combine(session.Home, $"painted-{Guid.NewGuid():N}.png");
        var preview = await session.RunAsync(
            Built.Program("thermi"), ["preview", snapshot, painted, "1", Settings.PathOf(session), "--icon", icon], cancel: cancel);
        Assert.True(preview.Exit == 0, preview.Error);
        return painted;
    }

    private sealed record Icon((string Name, string Path) At, string Id, string Shows, string Picture);

    private sealed record Shown(string Screen, string Bar, IReadOnlyList<Icon> Icons);
}
