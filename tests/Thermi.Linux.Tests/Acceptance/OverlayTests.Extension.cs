using System.Text.Json.Nodes;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>The overlay on GNOME while thermi's Shell extension is of another version or missing.</summary>
public sealed partial class OverlayTests
{
    [Fact(Timeout = Bound.Test)]
    public async Task AnExtensionOfAnotherVersionShowsNothing()
    {
        var cancel = TestContext.Current.CancellationToken;
        var other = Directory.CreateTempSubdirectory("thermi-extension-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFiles(TestShell.Thermi))
            {
                File.Copy(file, Path.Combine(other, Path.GetFileName(file)));
            }

            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(other, "metadata.json"), cancel))!;
            metadata["thermi-protocol"] = (int)metadata["thermi-protocol"]! - 1;
            await File.WriteAllTextAsync(Path.Combine(other, "metadata.json"), metadata.ToJsonString(), cancel);
            // Another protocol's signals and answers may have other shapes: this one sends Ready and Pointer with nothing in
            // them once thermi takes its name, and counts who asks its monitors over the bus, apart from its own signal.
            var code = await File.ReadAllTextAsync(Path.Combine(other, "extension.js"), cancel);
            const string Appeared = "this._owner = owner;";
            const string Monitors = "    Monitors() {";
            const string Signal = "[this.Monitors()]";
            Assert.Contains(Appeared, code, StringComparison.Ordinal);
            Assert.Contains(Monitors, code, StringComparison.Ordinal);
            Assert.Contains(Signal, code, StringComparison.Ordinal);
            var sends = "['Ready', 'Pointer'].forEach(signal => this._emit(signal, '()', []));";
            var counts = "    Monitors(own) { if (!own) global.thermiMonitorsAsked = (global.thermiMonitorsAsked ?? 0) + 1;";
            code = code.Replace(Appeared, $"{Appeared} {sends}", StringComparison.Ordinal)
                .Replace(Signal, "[this.Monitors(true)]", StringComparison.Ordinal)
                .Replace(Monitors, counts, StringComparison.Ordinal);
            await File.WriteAllTextAsync(Path.Combine(other, "extension.js"), code, cancel);

            await AnotherVersionShowsNothingAsync(other, cancel);
            await NoneIsInstalledFromTheStoreAsync(cancel);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

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
    // with no button.
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
    }
}
