using System.Text.Json.Nodes;
using Thermi.Core.Agents;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Linux.Tests.Desktops;
using Thermi.Tests.Contract;

namespace Thermi.Linux.Tests.Acceptance;

/// <summary>
/// AI agents and other programs on Linux (<c>docs/specs/006-linux/spec.md</c>, R2, R15 and R16): <c>thermi-cli</c> from
/// each shell, the SDK, and a settings file that crosses from Windows and back.
/// </summary>
public sealed class AgentTests
{
    // Each command of spec 003 that answers with no edit, and what it ends with when the service serves and no overlay runs.
    private static readonly (string[] Args, string? Schema, int Exit)[] Commands =
    [
        (["status", "--schema", "1"], "status", 0),
        (["read", "--schema", "1"], "read", 0),
        (["sensors", "--schema", "1"], "sensors", 0),
        (["monitors"], null, 0),
        (["config"], null, 0),
        (["looks"], null, 0),
        (["read", "no.such.metric"], null, 2),
    ];

    // How each shell runs a command and says the code it ended with, after what it printed.
    private static readonly (string Shell, string Code)[] Shells = [("bash", "$?"), ("zsh", "$?"), ("fish", "$status")];

    [Fact(Skip = "Waiting for its code")]
    public async Task TheCliAnswersOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        var units = await UserUnits.StartAsync(session, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);

        // thermi-cli by its name, found on the session's PATH as the package puts it there.
        foreach (var (shell, code) in Shells)
        {
            foreach (var (args, schema, exit) in Commands)
            {
                var said = await session.RunAsync(shell, ["-c", $"thermi-cli {string.Join(' ', args)}; echo \"ended {code}\""], cancel: cancel);
                var ended = said.Out.TrimEnd().Split('\n')[^1];
                Assert.Equal($"ended {exit}", ended);
                var json = said.Out[..^(ended.Length + 1)];
                Assert.Empty(schema is null ? [] : SchemaCheck.Findings(AgentSchemas.Current[schema], JsonNode.Parse(json)));
            }
        }

        Assert.Equal(("running", false), await session.SdkStatusAsync(cancel));
        // The one monitor, with the top bar the desktop keeps on it.
        var monitors = JsonNode.Parse((await session.RunAsync("thermi-cli", ["monitors"], cancel: cancel)).Out)!["monitors"]!.AsArray();
        Assert.True((bool)Assert.Single(monitors)!["taskbar"]!);
        await StartsAtSignInAsync(session, units, cancel);
        await CannotShowAsync(cancel);
    }

    [Fact(Skip = "Waiting for its code")]
    public async Task AWindowsFileLosesNothingOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        // A file Windows wrote, with hotkeys, tray icons and the window's place, and an FPS item, which Linux does not read;
        // agents may change it, as a person lets them.
        var file = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Built.Root, "tests", "Thermi.Tests", "Configs", "v6.json"), cancel))!;
        file.AsObject().Remove("agents");
        file["overlays"]![0]!["items"]!.AsArray().Add(new JsonObject { ["id"] = Metrics.Fps.Id, ["metric"] = Metrics.Fps.Id });
        var windows = file.ToJsonString();
        Directory.CreateDirectory(Path.GetDirectoryName(Settings.PathOf(session))!);
        await File.WriteAllTextAsync(Settings.PathOf(session), windows, cancel);
        var feed = await SensorFeed.ServeSteadyAsync(session, SensorFeed.Desktop, cancel);

        // The overlay and the window read it, and an agent edits it while both run.
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var window = await session.StartAsync(Built.Program("thermi"), ["settings", "--trace"], cancel: cancel);
        await Traced.FirstAsync(window.Error, Traced.WindowShown, cancel);
        var applied = Traced.NextAsync(thermi.Error, Traced.EditApplied, cancel);
        var edited = await session.RunAsync("thermi-cli", ["edit", """{"kind":"overlay","overlay":0,"field":"size","value":1.5}"""], cancel: cancel);
        await applied;
        var written = JsonNode.Parse(await File.ReadAllTextAsync(Settings.PathOf(session), cancel))!;
        var status = JsonNode.Parse((await session.RunAsync("thermi-cli", ["status", "--schema", "1"], cancel: cancel)).Out)!;

        Assert.Equal(0, edited.Exit);
        var expected = JsonNode.Parse(windows)!;
        expected["overlays"]![0]!["size"] = 1.5;
        Assert.True(JsonNode.DeepEquals(expected, written), $"The file lost or changed what it held:\n{written}");
        Assert.Null(ThermiConfigJson.Read(written.ToJsonString()).Problem);
        Assert.Contains(status["problems"]!.AsArray(), problem => (string?)problem!["code"] == "frames-not-read");
        Assert.Equal(0, await FpsTakesRoomAsync(desktop, written, feed.Snapshot, cancel));
    }

    // The FPS item takes no room: the first overlay paints the same with it and without it.
    private static async Task<long> FpsTakesRoomAsync(TestDesktop desktop, JsonNode written, string snapshot, CancellationToken cancel)
    {
        var without = written.DeepClone();
        var items = without["overlays"]![0]!["items"]!.AsArray();
        items.Remove(items.Single(item => (string?)item!["id"] == Metrics.Fps.Id));
        var withoutFile = Path.Combine(desktop.Session.Home, "without-fps.json");
        await File.WriteAllTextAsync(withoutFile, without.ToJsonString(), cancel);
        var with = await desktop.PreviewAsync(snapshot, 1, cancel);
        var other = await desktop.PreviewAsync(snapshot, 1, cancel, withoutFile);
        var (width, height) = await Pictures.SizeAsync(desktop.Session, with, cancel);
        Assert.Equal((width, height), await Pictures.SizeAsync(desktop.Session, other, cancel));
        return await Pictures.ChangedAsync(desktop.Session, with, other, (0, 0, width, height), cancel);
    }

    // start-with-windows keeps its name on Linux and enables the overlay's user unit, or disables it, never starting it.
    private static async Task StartsAtSignInAsync(OwnSession session, UserUnits units, CancellationToken cancel)
    {
        foreach (var (on, enabled) in new[] { ("on", "enabled"), ("off", "disabled") })
        {
            Assert.Equal(0, (await session.RunAsync("thermi-cli", ["start-with-windows", on], cancel: cancel)).Exit);
            Assert.Equal(enabled, await units.SystemctlAsync(["is-enabled", "thermi.service"], cancel));
            Assert.Equal("inactive", await units.SystemctlAsync(["is-active", "thermi.service"], cancel));
        }
    }

    // In a Wayland session with neither GNOME Shell nor layer surfaces, status says the overlay cannot show there.
    private static async Task CannotShowAsync(CancellationToken cancel)
    {
        await using var bare = await TestDesktop.StartAsync(DesktopKind.BareWayland, [new TestMonitor(1920, 1080)], cancel);
        await SensorFeed.ServeAsync(bare.Session, SensorFeed.Desktop, cancel);
        var status = JsonNode.Parse((await bare.Session.RunAsync("thermi-cli", ["status", "--schema", "1"], cancel: cancel)).Out)!;
        Assert.Contains(status["problems"]!.AsArray(), problem => (string?)problem!["code"] == "overlay-cannot-show");
    }
}
