using System.Text.Json.Nodes;
using Thermi.MacOS.Tests.Desktop;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>
/// thermi's app, its disk image and its cask (<c>docs/specs/008-macos/spec.md</c>, R1, R3, R4 and R16), from what one
/// build packed in <see cref="Packed"/>, and the updates that replace the app.
/// </summary>
public sealed partial class PackageTests
{
    /// <summary>
    /// Where the build puts what it packed for the Mac: <c>thermi.dmg</c>, the cask, <c>thermi.rb</c>, and the apps an update
    /// test installs, each in a folder of its name.
    /// </summary>
    public static readonly string Packed = Path.Combine(Built.Root, "artifacts", "macos");

    // The only entitlements .NET's runtime needs under the hardened runtime: its JIT writes code into memory it maps.
    private static readonly string[] Entitlements = ["com.apple.security.cs.allow-jit"];

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheAppIsPackedAsItSays()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(null, cancel);
        var session = mac.Session;
        var mounted = Path.Combine(session.Temporary, "dmg");
        string[] attach = ["attach", "-nobrowse", "-readonly", "-mountpoint", mounted, Path.Combine(Packed, "thermi.dmg")];
        var attached = await session.RunAsync("hdiutil", attach, cancel: cancel);
        var app = Path.Combine(mounted, "thermi.app");
        var program = Path.Combine(app, "Contents", "MacOS", "thermi");

        var archs = await session.RunAsync("lipo", ["-archs", program], cancel: cancel);
        var signed = await session.RunAsync("codesign", ["--verify", "--deep", "--strict", "--verbose=2", app], cancel: cancel);
        var flags = await session.RunAsync("codesign", ["-dv", "--verbose=4", app], cancel: cancel);
        var granted = Path.Combine(session.Temporary, "entitlements.plist");
        await session.RunAsync("codesign", ["-d", "--entitlements", granted, "--xml", app], cancel: cancel);
        var entitlements = await PlistAsync(session, granted, cancel);
        var info = await PlistAsync(session, Path.Combine(app, "Contents", "Info.plist"), cancel);
        var gatekeeper = await session.RunAsync("spctl", ["--assess", "--type", "execute", "--verbose", app], cancel: cancel);
        await session.RunAsync("hdiutil", ["detach", mounted], cancel: cancel);

        Assert.Equal(0, attached.Exit);
        Assert.Equal("arm64", archs.Out.Trim());
        Assert.Equal(0, signed.Exit);
        // codesign writes what it says of a signature on stderr: the hardened runtime is its flag "runtime".
        Assert.Contains("(runtime)", flags.Error, StringComparison.Ordinal);
        Assert.Equal(Entitlements, entitlements.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.True((bool)info["LSUIElement"]!);
        Assert.DoesNotContain(info.AsObject(), pair => pair.Key.EndsWith("UsageDescription", StringComparison.Ordinal));
        Assert.Equal(0, gatekeeper.Exit);
        await TheCaskInstallsAsync(mac, cancel);
    }

    // Homebrew installs the cask from the build's tap, into the session's own Applications, links thermi-cli onto the PATH
    // and runs thermi register, which loads both agents.
    private static async Task TheCaskInstallsAsync(TestMac mac, CancellationToken cancel)
    {
        var session = mac.Session;
        var applications = Directory.CreateDirectory(Path.Combine(session.Home, "Applications")).FullName;
        var brew = new Dictionary<string, string> { ["HOMEBREW_CASK_OPTS"] = $"--appdir={applications}", ["HOMEBREW_NO_AUTO_UPDATE"] = "1" };
        var installed = await session.RunAsync("brew", ["install", "--cask", Path.Combine(Packed, "thermi.rb")], brew, cancel);
        var cli = await session.RunAsync("/bin/zsh", ["-lc", "command -v thermi-cli"], brew, cancel);
        var (gui, user) = await session.DomainsAsync(cancel);
        var service = await Launchd.PrintAsync(session, user, Launchd.ServiceLabel, cancel);
        var overlay = await Launchd.PrintAsync(session, gui, Launchd.OverlayLabel, cancel);
        var removed = await session.RunAsync("brew", ["uninstall", "--cask", "thermi"], brew, cancel);

        Assert.Equal(0, installed.Exit);
        Assert.EndsWith("/thermi-cli", cli.Out.Trim(), StringComparison.Ordinal);
        Assert.NotNull(service);
        Assert.NotNull(overlay);
        Assert.Equal(0, removed.Exit);
        Assert.Null(await Launchd.PrintAsync(session, gui, Launchd.OverlayLabel, cancel));
    }

    /// <summary>A property list, as <c>plutil</c> turns it into JSON.</summary>
    public static async Task<JsonNode> PlistAsync(OwnSession session, string plist, CancellationToken cancel)
    {
        var json = await session.RunAsync("plutil", ["-convert", "json", "-o", "-", plist], cancel: cancel);
        return json.Exit == 0 ? JsonNode.Parse(json.Out)! : throw new InvalidOperationException($"plutil did not read {plist}: {json.Error}");
    }
}
