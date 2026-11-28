using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Thermi.Core.Settings;
using Thermi.MacOS.Tests.Desktop;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>The service's updates of the app on a test channel, the overlay's hand-over, and the owner's levers with a Mac app.</summary>
public sealed partial class PackageTests
{
    public enum Offered
    {
        SignedByThermi,
        OtherTeam,
        NotNotarized,
        CannotWrite,
        FailsAtStart,
    }

    // Each row's app on the channel, as the build packed it: thermi's newer one, the same signed by another team, one
    // signed by thermi and not notarized, and a newer one whose thermi ends at once with 1.
    private static string AppOf(Offered row) => Path.Combine(Packed, row switch
    {
        Offered.SignedByThermi or Offered.CannotWrite => "newer",
        Offered.OtherTeam => "other-team",
        Offered.NotNotarized => "not-notarized",
        Offered.FailsAtStart => "fails-at-start",
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown app."),
    });

    [Theory(Timeout = Bound.Test)]
    [InlineData(Offered.SignedByThermi, Skip = "Waiting for its code")]
    [InlineData(Offered.OtherTeam, Skip = "Waiting for its code")]
    [InlineData(Offered.NotNotarized, Skip = "Waiting for its code")]
    [InlineData(Offered.CannotWrite, Skip = "Waiting for its code")]
    [InlineData(Offered.FailsAtStart, Skip = "Waiting for its code")]
    public async Task UpdatesReplaceTheAppOnlyWhenTrusted(Offered row)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(null, cancel);
        var session = mac.Session;
        var installed = await InstallAsync(session, cancel);
        var levers = new Levers(session);
        await levers.RunAsync(["publish", "--channel", "stable", "--version", "1.1.0", "--app", Path.Combine(AppOf(row), "thermi.app.zip")], cancel);
        var (checkedIt, before, after, status) = await CheckAsync(session, installed, levers.Url, row == Offered.CannotWrite, cancel);
        var previous = Path.Combine(session.Home, "Library", "Application Support", "thermi", "previous", "thermi.app");
        var log = string.Concat(Directory.EnumerateFiles(Path.Combine(session.Home, "Library", "Logs", "thermi")).Select(File.ReadAllText));

        // Only thermi's own newer app, notarized, replaces it, whole, with the previous kept; one that fails at start is
        // taken back; the rest leave it as it was and say why in the log, or in About when the app cannot be written.
        Assert.Equal(0, checkedIt.Exit);
        Assert.Equal(row == Offered.SignedByThermi ? "1.1.0" : before, after);
        Assert.Equal(row is Offered.SignedByThermi or Offered.FailsAtStart, Directory.Exists(previous));
        Assert.Equal(0, (await session.RunAsync("codesign", ["--verify", "--deep", "--strict", installed], cancel: cancel)).Exit);
        Assert.True(row is not (Offered.OtherTeam or Offered.NotNotarized or Offered.FailsAtStart) || log.Contains("1.1.0", StringComparison.Ordinal), log);
        Assert.Equal(row == Offered.CannotWrite, status["updates"]?["problem"] is not null);
    }

    // The service checks the channel for the installed app, and what status says after, in an Applications its user may
    // not write when the row says so.
    private static async Task<(Ran Checked, string Before, string After, JsonNode Status)> CheckAsync(
        OwnSession session, string installed, string channels, bool cannotWrite, CancellationToken cancel)
    {
        var applications = Path.GetDirectoryName(installed)!;
        var writable = File.GetUnixFileMode(applications);
        try
        {
            if (cannotWrite)
            {
                File.SetUnixFileMode(applications, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            var before = await VersionAsync(session, installed, cancel);
            var service = Path.Combine(installed, "Contents", "MacOS", "thermi-sensors");
            var checkedIt = await session.RunAsync(service, ["--check-updates", "--from", channels], cancel: cancel);
            var status = JsonNode.Parse((await session.RunAsync("thermi-cli", ["status", "--schema", "1"], cancel: cancel)).Out)!;
            return (checkedIt, before, await VersionAsync(session, installed, cancel), status);
        }
        finally
        {
            // The session deletes its home as it ends, which a folder its user cannot write keeps.
            File.SetUnixFileMode(applications, writable);
        }
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheOverlayHandsOverAfterAnUpdateOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var session = mac.Session;
        var installed = await InstallAsync(session, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0].Visible;
        var overlays = new[] { new Position(main.X + 100, main.Y + 100), new Position(main.X + 100, main.Y + 400) };
        await mac.WriteSettingsAsync(ThermiConfig.New with { Overlays = [.. overlays.Select(at => OverlayConfig.First with { Position = at })] }, cancel);
        var first = session.Start(Path.Combine(installed, "Contents", "MacOS", "thermi"), ["--trace"]);
        await Traced.FirstAsync(first.Error, Traced.Shown, cancel);
        var window = session.Start(Path.Combine(installed, "Contents", "MacOS", "thermi"), ["settings", "--trace"]);
        await Traced.FirstAsync(window.Error, Traced.WindowShown, cancel);
        var area = (main.X + 100, main.Y + 100, 300, 340);
        var shown = await Screen.CaptureAsync(session, area, cancel);

        // The service replaced the app, as it does, whole; the overlay waits for the window to close, then for a quiet
        // moment, with no input and nothing changing on screen, and hands over to the new version.
        Replace(installed, Path.Combine(Packed, "newer", "thermi.app"));
        await TestMac.PaintedAsync(first, cancel);
        var whileOpen = await PidsAsync(session, installed, cancel);
        window.Kill();
        await first.WaitForExitAsync(cancel);
        var handedOver = await PidsAsync(session, installed, cancel);
        var after = await Screen.CaptureAsync(session, area, cancel);

        Assert.Contains(first.Id, whileOpen);
        Assert.Equal(0, first.ExitCode);
        Assert.NotEqual(first.Id, Assert.Single(handedOver));
        Assert.Equal(0, after.Differing(shown));
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task TheLeversPublishTheMacApp()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var levers = new Levers(session);
        var newer = await AppZipAsync(Path.Combine(Packed, "newer", "thermi.app.zip"), cancel);
        // The build's own app, as an earlier version that stable offers first, for the rollback to go back to.
        var zipped = Path.Combine(session.Temporary, "thermi.app.zip");
        Assert.Equal(0, (await session.RunAsync("ditto", ["-c", "-k", "--keepParent", Built.App, zipped], cancel: cancel)).Exit);
        var earlier = await AppZipAsync(zipped, cancel);
        await levers.RunAsync(["publish", "--channel", "stable", "--version", "1.0.0", "--app", zipped], cancel);

        // What each lever leaves its channel offering, and stable's in the tap's cask; disabled, a channel offers nothing new.
        (string[] Lever, string Channel, string Version, AppZip? App)[] pulled =
        [
            (["publish", "--channel", "beta", "--version", "1.1.0", "--app", newer.Path], "beta", "1.1.0", newer),
            (["promote", "--from", "beta", "--to", "stable"], "stable", "1.1.0", newer),
            (["rollback", "--channel", "stable"], "stable", "1.0.0", earlier),
            (["disable", "--channel", "stable"], "stable", "1.0.0", null),
        ];

        foreach (var (lever, channel, version, app) in pulled)
        {
            await levers.RunAsync(lever, cancel);
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(levers.Channels, channel, "manifest.json"), cancel))!;
            var cask = await File.ReadAllTextAsync(Path.Combine(levers.Tap, "Casks", "thermi.rb"), cancel);
            // Each manifest is signed as on Windows, and names the Mac's app while a release is out.
            Assert.Equal(0, (await levers.VerifyAsync(channel, cancel)).Exit);
            Assert.True(app is null || Offers(manifest["release"], version, app), $"{lever[0]} wrote {manifest}");
            Assert.True(app is null || channel != "stable" || (cask.Contains($"version \"{version}\"", StringComparison.Ordinal)
                && cask.Contains($"sha256 \"{app.Sha}\"", StringComparison.Ordinal)), $"{lever[0]} left the cask {cask}");
        }
    }

    private static bool Offers(JsonNode? release, string version, AppZip app) =>
        (string?)release?["version"] == version && (long?)release?["mac"]?["size"] == app.Size && (string?)release?["mac"]?["sha256"] == app.Sha;

    // A zipped app, as a manifest and the cask name it: its size and SHA-256.
    private static async Task<AppZip> AppZipAsync(string path, CancellationToken cancel)
    {
        await using var zip = File.OpenRead(path);
        return new AppZip(path, zip.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(zip, cancel)));
    }

    private sealed record AppZip(string Path, long Size, string Sha);

    // The build's app, as the person installed it, in the session's own Applications.
    private static async Task<string> InstallAsync(OwnSession session, CancellationToken cancel)
    {
        var installed = Path.Combine(Directory.CreateDirectory(Path.Combine(session.Home, "Applications")).FullName, "thermi.app");
        var copied = await session.RunAsync("ditto", [Built.App, installed], cancel: cancel);
        return copied.Exit == 0 ? installed : throw new InvalidOperationException($"ditto copied no app: {copied.Error}");
    }

    // Over the installed app, as the service swaps it: the new one beside it, then renamed over it at once.
    private static void Replace(string installed, string newer)
    {
        var beside = installed + ".new";
        CopyFolder(newer, beside);
        Directory.Move(installed, installed + ".old");
        Directory.Move(beside, installed);
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    // The pids of the installed app's thermi that run, the overlay's and the window's.
    private static async Task<int[]> PidsAsync(OwnSession session, string installed, CancellationToken cancel)
    {
        var found = await session.RunAsync("pgrep", ["-f", Path.Combine(installed, "Contents", "MacOS", "thermi")], cancel: cancel);
        return [.. found.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(pid => int.Parse(pid, CultureInfo.InvariantCulture))];
    }

    private static async Task<string> VersionAsync(OwnSession session, string app, CancellationToken cancel) =>
        (string)(await PlistAsync(session, Path.Combine(app, "Contents", "Info.plist"), cancel))["CFBundleShortVersionString"]!;

    /// <summary>The owner's levers of spec 004 on a channels folder of the session's, with keys of its own and a tap.</summary>
    private sealed class Levers(OwnSession session)
    {
        public string Channels { get; } = Path.Combine(session.Home, "channels");

        public string Tap { get; } = Path.Combine(session.Home, "tap");

        public string Url => new Uri(Channels).AbsoluteUri;

        private string Keys => Path.Combine(session.Home, "keys");

        public async Task RunAsync(IEnumerable<string> args, CancellationToken cancel)
        {
            if (!Directory.Exists(Keys))
            {
                await PullAsync(["keys", "--out", Keys], cancel);
            }

            await PullAsync(
                [.. args, "--folder", Channels, "--tap", Tap, "--base-url", Url, "--key", Path.Combine(Keys, "release.key.pem"), "--trust", Keys], cancel);
        }

        public Task<Ran> VerifyAsync(string channel, CancellationToken cancel) =>
            session.RunAsync("pwsh", ["-NoProfile", "-File", Script, "status", "--folder", Channels, "--channel", channel, "--trust", Keys], cancel: cancel);

        private static string Script => Path.Combine(Built.Root, "scripts", "updates.ps1");

        private async Task PullAsync(string[] args, CancellationToken cancel)
        {
            var pulled = await session.RunAsync("pwsh", ["-NoProfile", "-File", Script, .. args], cancel: cancel);
            Assert.True(pulled.Exit == 0, pulled.Error);
        }
    }
}
