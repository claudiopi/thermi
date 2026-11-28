using System.Text.Json.Nodes;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Linux.Tests.Desktops;

namespace Thermi.Linux.Tests.Acceptance;

public sealed partial class PackageTests
{
    [Fact(Timeout = Bound.Test)]
    public async Task TheOverlayHandsOverAfterAnUpdateOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        // The packed build in a folder of the session's, as a package puts it in /usr/lib/thermi.
        var installed = Path.Combine(session.Home, "installed");
        await InstallAsync(session, Built.Packages, installed, replace: false, cancel);
        var config = ThermiConfig.New with { Overlays = [Scene.Overlay with { Position = Scene.OnWindow }] };
        await desktop.WriteSettingsAsync(config, cancel);
        var units = await UserUnits.StartAsync(session, cancel, Path.Combine(installed, "thermi"));
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);
        var started = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        await units.SystemctlAsync(["start", "thermi.service"], cancel);
        await started;
        var first = await units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi.service"], cancel);
        var window = await session.StartAsync(Path.Combine(installed, "thermi"), ["settings", "--trace"], cancel: cancel);
        await Traced.FirstAsync(window.Error, Traced.WindowShown, cancel);
        // The new window is the old one's successor, and writes where it did.
        var overlay = Traced.NextAsync(units.Trace, Traced.Shown, cancel);
        var successor = Traced.NextAsync(window.Error, Traced.WindowShown, cancel);
        await InstallAsync(session, Built.Newer, installed, replace: true, cancel);

        // Every overlay hidden is a quiet moment, as the person cannot see them change; the idle time's is Core's rule's.
        var rows = Menus.OfOverlay(config, 0, new MenuState(false, false, ServiceState.Running, null, [], false), config.Overlays[0].Items[0].Id);
        await OverlayTests.OpenMenuAsync(desktop, cancel);
        await Accessible.ChooseAsync(desktop, OverlayTests.IndexOf(rows, MenuEntry.HideOverlays), cancel);
        await window.WaitForExitAsync(cancel);
        await successor;
        await overlay;
        var windows = (await Accessible.WindowsAsync(session, cancel)).Where(shown => shown.Program == "thermi").ToList();
        var cli = Path.Combine(installed, "thermi-cli");
        var version = (await session.RunAsync(cli, ["--version"], cancel: cancel)).Error.Trim();
        var about = JsonNode.Parse((await session.RunAsync(cli, ["status", "--schema", "1"], cancel: cancel)).Out)!;

        Assert.NotEqual(first, await units.SystemctlAsync(["show", "-p", "MainPID", "--value", "thermi.service"], cancel));
        Assert.Single(await units.ProcessesAsync(cancel));
        Assert.NotEqual(window.Id, Assert.Single(windows).Process);
        // The new files run: their version, which status reads from Core as About does; the channel and the system's
        // updates About says only of a build in /usr/lib/thermi, which StatusAboutTests and the packages' containers prove.
        Assert.StartsWith(Built.PackedVersion(Built.Newer), version, StringComparison.Ordinal);
        Assert.Equal(version, (string?)about["version"]);
    }

    // Both packages' program files from the packed tarball in <paramref name="packages"/>, as they install them in
    // /usr/lib/thermi, put in <paramref name="to"/>; replacing what is there, each renamed over its old one, as dpkg does.
    private static async Task InstallAsync(OwnSession session, string packages, string to, bool replace, CancellationToken cancel)
    {
        var tarball = Directory.GetFiles(packages, "thermi-*-linux-x64.tar.gz").Single();
        var unpacked = Directory.CreateDirectory(Path.Combine(session.Home, Path.GetFileName(tarball) + ".d")).FullName;
        var made = await session.RunAsync("tar", ["-xzf", tarball, "-C", unpacked], cancel: cancel);
        Assert.True(made.Exit == 0, made.Error);
        var tree = Directory.GetDirectories(unpacked).Single();
        foreach (var package in new[] { "thermi-sensors", "thermi" })
        {
            Built.Copy(Path.Combine(tree, package, "usr", "lib", "thermi"), to, replace);
        }
    }
}
