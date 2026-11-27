using System.Globalization;
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
    // Each command of spec 003 a shell runs to its end, and what it ends with when the service serves and no overlay runs;
    // an edit and a look, each the shell's own, change the file as no overlay holds it. find-cpu-fan loads the PC for its
    // run, and start-with-windows is checked below.
    private static (string Args, string? Schema, int Exit)[] CommandsOf(string shell, double size) =>
    [
        ("status --schema 1", "status", 0),
        ("read --schema 1", "read", 0),
        ("sensors --schema 1", "sensors", 0),
        ("monitors", null, 0),
        ("config", null, 0),
        ("looks", null, 0),
        ($"look save {shell} --overlay 0", null, 0),
        (string.Create(CultureInfo.InvariantCulture, $$"""edit '{"kind":"overlay","overlay":0,"field":"size","value":{{size}}}'"""), null, 0),
        ("read no.such.metric", null, 2),
    ];

    // How each shell runs a command and says the code it ended with, after what it printed, and the size its edit sets.
    private static readonly (string Shell, string Code, double Size)[] Shells = [("bash", "$?", 1.25), ("zsh", "$?", 1.5), ("fish", "$status", 1.75)];

    [Fact(Skip = "Waiting for its code")]
    public async Task TheCliAnswersOnLinux()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var desktop = await TestDesktop.StartAsync(DesktopKind.Gnome, [new TestMonitor(1920, 1080)], cancel);
        var session = desktop.Session;
        var units = await UserUnits.StartAsync(session, cancel);
        await SensorFeed.ServeAsync(session, SensorFeed.Desktop, cancel);

        // thermi-cli by its name, found on the session's PATH as the package puts it there.
        foreach (var (shell, code, size) in Shells)
        {
            foreach (var (args, schema, exit) in CommandsOf(shell, size))
            {
                var said = await session.RunAsync(shell, ["-c", $"thermi-cli {args}; echo \"ended {code}\""], cancel: cancel);
                var lines = said.Out.TrimEnd().Split('\n');
                Assert.Equal($"ended {exit}", lines[^1]);
                // A command that ends with 0 prints one JSON document on stdout, and one that ends with 2 nothing.
                var json = string.Join('\n', lines[..^1]);
                var document = json.Length == 0 ? null : JsonNode.Parse(json);
                Assert.Equal(exit == 0, document is not null);
                Assert.Empty(schema is null ? [] : SchemaCheck.Findings(AgentSchemas.Current[schema], document));
            }

            await WatchesAsync(session, shell, code, cancel);
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
        // The window shows an item missing with the catalog's why, the one read answers for the item too.
        var fps = JsonNode.Parse((await session.RunAsync("thermi-cli", ["read", $"item:{Metrics.Fps.Id}"], cancel: cancel)).Out)!;

        Assert.Equal(0, edited.Exit);
        var expected = JsonNode.Parse(windows)!;
        expected["overlays"]![0]!["size"] = 1.5;
        Assert.True(JsonNode.DeepEquals(expected, written), $"The file lost or changed what it held:\n{written}");
        Assert.Null(ThermiConfigJson.Read(written.ToJsonString()).Problem);
        Assert.Contains(status["problems"]!.AsArray(), problem => (string?)problem!["code"] == "frames-not-read");
        Assert.Equal("frames-unread", (string?)Assert.Single(fps["values"]!.AsArray())!["why"]);
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

    // watch prints read's document a line a second, each valid, until its input closes, and then ends with 0.
    private static async Task WatchesAsync(OwnSession session, string shell, string code, CancellationToken cancel)
    {
        var watch = await session.StartAsync(shell, ["-c", $"thermi-cli watch; echo \"ended {code}\""], cancel: cancel);
        for (var line = 0; line < 2; line++)
        {
            Assert.Empty(SchemaCheck.Findings(AgentSchemas.Current["read"], JsonNode.Parse((await watch.Out.ReadLineAsync(cancel))!)));
        }

        watch.Input.Close();
        string? last = null;
        while (await watch.Out.ReadLineAsync(cancel) is { } next)
        {
            last = next;
        }

        await watch.WaitForExitAsync(cancel);
        Assert.Equal("ended 0", last);
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
