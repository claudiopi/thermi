using System.Globalization;
using Thermi.Core;
using Thermi.Core.Settings;
using Thermi.Linux.Tests.Acceptance;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Hotkeys;

/// <summary>
/// What a desktop off GNOME gives of thermi's hotkeys: none, and says so, where it has no way to give them, as Sway with no
/// GlobalShortcuts portal; on X11, each with a mouse button held too, as the X server tells that button with the keys.
/// </summary>
public sealed class GrabTests
{
    private static readonly HotkeysConfig Toggle = new() { Toggle = "Ctrl+Shift+J" };

    public enum Case
    {
        NoPortal,
        ButtonHeld,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Case.NoPortal)]
    [InlineData(Case.ButtonHeld)]
    public async Task TheDesktopsGrabSaysWhatItGave(Case row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(row == Case.NoPortal ? DesktopKind.Sway : DesktopKind.X11, [new TestMonitor(1280, 720)], cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }], Hotkeys = Toggle }, cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        if (row == Case.NoPortal)
        {
            // Said once for all of them, as no program holds one.
            await Traced.FirstAsync(thermi.Error, Traced.HotkeysRefused, cancel);
            var asked = await desktop.Session.RunAsync(Built.Program("Thermi.Linux.Tests"), [DesktopTests.HotkeyStatus], cancel: cancel);
            var hotkeys = (await Settings.ReadAsync(desktop.Session, cancel)).Hotkeys;
            var failed = Core.Hotkeys.Trusted(long.Parse(asked.Out.Trim(), CultureInfo.InvariantCulture), hotkeys);
            Assert.Equal(HotkeyFailures.Toggle | HotkeyFailures.DesktopRefused, failed);
            Assert.DoesNotContain(thermi.Error.Text.Split('\n'), line => Traced.Of(line)?.Event == Traced.HotkeyRefused);
            return;
        }

        var hidden = Traced.NextAsync(thermi.Error, Traced.Hidden, cancel);
        Assert.Equal(0, (await desktop.Session.RunAsync("xdotool", ["mousedown", "1"], cancel: cancel)).Exit);
        await desktop.Input.PressAsync("Control_L+Shift_L+j", cancel);
        await hidden;
        Assert.Equal(0, (await desktop.Session.RunAsync("xdotool", ["mouseup", "1"], cancel: cancel)).Exit);
    }
}
