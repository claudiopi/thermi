using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Gnome;

/// <summary>
/// A key on thermi's icon in GNOME's top bar, once the bar's focus is on it, is the press Windows' tray makes of it: Enter
/// and Space a click, which brings hidden overlays back and otherwise opens thermi's menu, and the Menu key the menu always.
/// Any other key is the Shell's, as on its own icons: an "a" pressed first, were it a click, would show the hidden overlays
/// before the Menu key opens the menu.
/// </summary>
public sealed class IconKeyTests
{
    private static readonly TestMonitor Monitor = new(1920, 1080);

    // Rows: each key's press, told apart where the overlays are hidden, and Enter where they show; the Shell's menu is
    // thermi's.
    [Theory(Timeout = Bound.Test)]
    [InlineData("Return", false, "gnome-shell")]
    [InlineData("space", true, "shown")]
    [InlineData("Menu", true, "gnome-shell")]
    public async Task AKeyOnThermisIconIsTheTraysPress(string key, bool hidden, string answer)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], cancel);
        var session = desktop.Session;
        var overlay = Scene.Overlay with { Position = Scene.OnWindow };
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [overlay], Hotkeys = new HotkeysConfig { Toggle = "Ctrl+Shift+J" } }, cancel);
        _ = await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        await Traced.NextAsync(thermi.Error, Traced.Paint, cancel);
        if (hidden)
        {
            var hiding = Traced.NextAsync(thermi.Error, Traced.Hidden, cancel);
            await desktop.Input.PressAsync("Control_L+Shift_L+j", cancel);
            await hiding;
        }

        await desktop.Input.ShellAsync($"Main.panel.statusArea['thermi-{TrayScene.ThermisId}'].grab_key_focus()", cancel);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        var shown = Traced.NextAsync(thermi.Error, Traced.Shown, either.Token);
        await desktop.Input.PressAsync("a", cancel);
        await desktop.Input.PressAsync(key, cancel);
        var menus = Accessible.OpenMenusAsync(session, either.Token);
        var first = await Task.WhenAny(shown, menus);
        await either.CancelAsync();
        // thermi showed its overlays again, or the wait for it says why it did not.
        var got = first == menus ? (await menus).Single().Program : shown.IsCompletedSuccessfully ? "shown" : shown.Exception?.InnerException?.Message;

        Assert.Equal(answer, got);
    }
}
