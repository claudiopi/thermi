using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// The overlay where it cannot show: on GNOME while thermi's Shell extension is of another version or missing, and on a
/// Wayland desktop with no surface for it.
/// </summary>
public sealed partial class OverlayTests
{
    [Fact(Timeout = Bound.Test)]
    public async Task AnExtensionOfAnotherVersionShowsNothing()
    {
        var cancel = TestContext.Current.CancellationToken;
        // An extension without the overlay's own calls in thermi's shapes, as one before them: it declares no Show, sends
        // Ready and Pointer with nothing in them once thermi takes its name, and counts who asks its monitors over the bus,
        // apart from its own signal.
        var other = await ExtensionBeforeAsync(
            code =>
            {
                const string Appeared = "this._owner = owner;";
                const string Monitors = "    Monitors() {";
                const string Signal = "[this.Monitors()]";
                const string Show = "<method name=\"Show\">";
                Assert.All([Appeared, Monitors, Signal, Show], part => Assert.Contains(part, code, StringComparison.Ordinal));
                var sends = "['Ready', 'Pointer'].forEach(signal => this._emit(signal, '()', []));";
                var counts = "    Monitors(own) { if (!own) global.thermiMonitorsAsked = (global.thermiMonitorsAsked ?? 0) + 1;";
                return code.Replace(Appeared, $"{Appeared} {sends}", StringComparison.Ordinal)
                    .Replace(Signal, "[this.Monitors(true)]", StringComparison.Ordinal)
                    .Replace(Monitors, counts, StringComparison.Ordinal)
                    .Replace(Show, "<method name=\"Paint\">", StringComparison.Ordinal);
            },
            cancel);
        try
        {
            await AnotherVersionShowsNothingAsync(other, cancel);
            await NoneIsInstalledFromTheStoreAsync(cancel);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    // After a package updates thermi, the Shell runs the extension it read at sign-in until the next. One of the protocol
    // before thermi's, with no more than the overlay's own calls, as monitors of the parts every protocol had, still shows
    // the overlays, on the top bar and on a window, and its notice asks to sign in again, with no button.
    [Fact(Timeout = Bound.Test)]
    public async Task AnEarlierExtensionStillShowsTheOverlays()
    {
        var cancel = TestContext.Current.CancellationToken;
        var earlier = await ExtensionBeforeAsync(
            code =>
            {
                const string Kept = "const keptBoxes = () => [Main.panel._centerBox, Main.panel._rightBox, Main.panel._leftBox];";
                const string Parts = "ddddddbbdddddd";
                Assert.All([Kept, Parts], part => Assert.Contains(part, code, StringComparison.Ordinal));
                var floor = FloorOnly().Replace(code, "");
                Assert.DoesNotContain("<method name=\"Menu\">", floor, StringComparison.Ordinal);
                return floor.Replace(Kept, "const keptBoxes = () => [Main.panel._centerBox, Main.panel._rightBox];", StringComparison.Ordinal)
                    .Replace(Parts, "ddddddbbdddd", StringComparison.Ordinal);
            },
            cancel);
        try
        {
            await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], earlier, cancel);
            var notices = await NoticeWatch.StartAsync(desktop, cancel);
            var told = notices.NextAsync(notice => notice.Actions.Count == 0, cancel);
            var scene = await Scene.ShowAsync(desktop, 1, fullScreen: false, stopped: false, cancel);

            Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnWindow, cancel));
            Assert.Equal(0, await scene.DifferingAtAsync(Scene.OnPanel, cancel));
            await told;
        }
        finally
        {
            Directory.Delete(earlier, recursive: true);
        }
    }

    // A package puts thermi's extension in place, which GNOME reads at the next sign-in and leaves off: thermi turns it on,
    // and the overlays show at once. One the person turned off stays off, and its notice says how to turn it on, with no
    // button.
    [Theory(Timeout = Bound.Test)]
    [InlineData(ExtensionStart.NeverOn, true)]
    [InlineData(ExtensionStart.TurnedOff, false)]
    public async Task ThePackagesExtensionShowsTheOverlaysUnlessTurnedOff(ExtensionStart start, bool shows)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], TestShell.Thermi, cancel, start: start);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        Task off = notices.NextAsync(notice => notice.Actions.Count == 0, cancel);
        var thermi = await desktop.StartThermiAsync(["--trace"], cancel);
        Task shown = Traced.FirstAsync(thermi.Error, Traced.Shown, cancel);
        var first = await Task.WhenAny(shown, off);
        var enabled = await desktop.Session.RunAsync("gsettings", ["get", "org.gnome.shell", "enabled-extensions"], cancel: cancel);

        Assert.Equal(shows, first == shown);
        Assert.Equal(shows, enabled.Out.Contains(ShellLink.Uuid, StringComparison.Ordinal));
    }

    // A copy of thermi's extension that says the protocol before its own, with its code changed as the test says.
    private static async Task<string> ExtensionBeforeAsync(Func<string, string> change, CancellationToken cancel)
    {
        var copy = Directory.CreateTempSubdirectory("thermi-extension-").FullName;
        foreach (var file in Directory.EnumerateFiles(TestShell.Thermi))
        {
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }

        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(copy, "metadata.json"), cancel))!;
        metadata["thermi-protocol"] = (int)metadata["thermi-protocol"]! - 1;
        await File.WriteAllTextAsync(Path.Combine(copy, "metadata.json"), metadata.ToJsonString(), cancel);
        var code = Path.Combine(copy, "extension.js");
        await File.WriteAllTextAsync(code, change(await File.ReadAllTextAsync(code, cancel)), cancel);
        return copy;
    }

    // What the Shell's interface declares past the overlay's own calls, which an extension before them lacks.
    [GeneratedRegex(
        @"<method name=""(Fade|Menu|Cursor|Copy|Watch|Activation)"">.*?</method>|<signal name=""(Key|Chosen)"">.*?</signal>",
        RegexOptions.Singleline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FloorOnly();

    private static async Task AnotherVersionShowsNothingAsync(string other, CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], other, cancel);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] }, cancel);
        await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        var update = notices.NextAsync("update-extension", cancel);
        var thermi = await desktop.StartThermiAsync([], cancel);

        var told = await update;
        var after = await desktop.ScreenshotAsync(cancel);
        // Its button is heard after the extension's Ready: thermi that read it still runs and asks GNOME for the update.
        var checking = notices.NextUpdateCheckAsync(cancel);
        await notices.ClickAsync(told, "update-extension", cancel);
        var asked = await Task.WhenAny(checking, thermi.WaitForExitAsync(cancel)) == checking;
        // The window takes the monitors the Shell tells only from an extension of its own protocol, as the overlay does, and
        // asks this one none: its map draws GDK's.
        var window = await desktop.Session.StartAsync(Built.Program("thermi"), ["settings", "--trace"], cancel: cancel);
        await Traced.FirstAsync(window.Error, Traced.WindowShown, cancel);
        var monitors = await desktop.Session.RunAsync(
            "gdbus", TestDesktop.Call("org.gnome.Shell", "/org/gnome/Shell", "Eval", "global.thermiMonitorsAsked ?? 0"), cancel: cancel);
        Assert.Equal(0, await Pictures.ChangedAsync(desktop.Session, before, after, UnderTheBar, cancel));
        Assert.True(asked, thermi.Error.Text);
        Assert.Equal("(true, '0')", monitors.Out.Trim());
    }

    // With no extension, thermi asks the Shell to install its own from extensions.gnome.org, which GNOME confirms itself,
    // and its notice offers to ask again. With its files in place that the running Shell has not read, as right after a
    // package put them there, it asks the same; the Shell, offline, installs nothing, so its notice says to sign in again,
    // with no button, and the person's settings turn the copy on for that sign-in.
    private static async Task NoneIsInstalledFromTheStoreAsync(CancellationToken cancel)
    {
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [Monitor], extension: null, cancel);
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        var asked = notices.NextInstallAsync(cancel);
        var offered = notices.NextAsync(ExtensionArrival.InstallAction, cancel);
        var missing = await desktop.StartThermiAsync([], cancel);
        var uuid = (string)JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(TestShell.Thermi, "metadata.json"), cancel))!["uuid"]!;
        Assert.Equal(uuid, await asked);
        await offered;
        missing.Kill();
        await missing.WaitForExitAsync(cancel);

        var placed = Directory.CreateDirectory(Path.Combine(desktop.Session.Variables["XDG_DATA_HOME"], "gnome-shell", "extensions", uuid)).FullName;
        foreach (var file in Directory.EnumerateFiles(TestShell.Thermi))
        {
            File.Copy(file, Path.Combine(placed, Path.GetFileName(file)));
        }

        var again = notices.NextInstallAsync(cancel);
        var told = notices.NextAsync(notice => notice.Actions.Count == 0, cancel);
        await desktop.StartThermiAsync([], cancel);
        Assert.Equal(uuid, await again);
        await told;
        var enabled = await desktop.Session.RunAsync("gsettings", ["get", "org.gnome.shell", "enabled-extensions"], cancel: cancel);
        Assert.Contains($"'{uuid}'", enabled.Out, StringComparison.Ordinal);
    }

    // Weston offers neither the extension nor layer surfaces: thermi shows nothing, says why in one notice, and not again
    // the next time it starts. A start with no notices to say it on says nothing, so the next says it; one whose GTK cannot
    // start, with no display, is no desktop without surfaces, and says nothing of them. Its unit does not start it again.
    private static async Task NeitherSurfaceShowsAsync(TestDesktop desktop, CancellationToken cancel)
    {
        await desktop.WriteSettingsAsync(ThermiConfig.New, cancel);
        async Task EndsAsync(Dictionary<string, string>? variables)
        {
            var thermi = await desktop.Session.StartAsync(Built.Program("thermi"), [], variables, cancel);
            await thermi.WaitForExitAsync(cancel);
            Assert.NotEqual(0, thermi.ExitCode);
        }

        // Failed, with no start pending, where a restart would wait in auto-restart.
        var units = await UserUnits.StartAsync(desktop.Session, cancel);
        await (await units.StartWaitingAsync(cancel)).WaitForExitAsync(cancel);
        Assert.Equal("failed", await units.SystemctlAsync(["show", "-p", "SubState", "--value", "thermi.service"], cancel));
        var notices = await NoticeWatch.StartAsync(desktop, cancel);
        await EndsAsync(new Dictionary<string, string> { ["WAYLAND_DISPLAY"] = "", ["DISPLAY"] = "" });
        await notices.SyncAsync(cancel);
        var noDisplay = notices.Count("no-overlay");
        for (var start = 0; start < 2; start++)
        {
            await EndsAsync(null);
        }

        await notices.SyncAsync(cancel);
        Assert.Equal((0, 1), (noDisplay, notices.Count("no-overlay")));
    }
}
