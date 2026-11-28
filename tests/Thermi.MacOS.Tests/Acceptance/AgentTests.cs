using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.Core.Agents;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.MacOS.Tests.Desktop;
using Thermi.Tests.Contract;

namespace Thermi.MacOS.Tests.Acceptance;

/// <summary>
/// AI agents and other programs on the Mac (<c>docs/specs/008-macos/spec.md</c>, R2, R22 and R23): <c>thermi-cli</c>
/// from each shell and over SSH, the SDK, and a settings file that crosses from Windows and Linux and back.
/// </summary>
public sealed class AgentTests
{
    // Each command of spec 003 a shell runs to its end, and what it ends with when the service serves and no overlay runs;
    // an edit and a look change the file as no overlay holds it. start-with-windows and find-cpu-fan are checked below.
    private static readonly (string Args, string? Schema, int Exit)[] Commands =
    [
        ("status --schema 1", "status", 0),
        ("read --schema 1", "read", 0),
        ("sensors --schema 1", "sensors", 0),
        ("config", null, 0),
        ("looks", null, 0),
        ("look save mine --overlay 0", null, 0),
        ("""edit '{"kind":"overlay","overlay":0,"field":"size","value":1.25}'""", null, 0),
        ("read no.such.metric", null, 2),
    ];

    public enum Cli
    {
        Zsh,
        Bash,
        OverSsh,
        OverlayOnRetina,
    }

    [Theory(Timeout = Bound.Test)]
    [InlineData(Cli.Zsh, Skip = "Waiting for its code")]
    [InlineData(Cli.Bash, Skip = "Waiting for its code")]
    [InlineData(Cli.OverSsh, Skip = "Waiting for its code")]
    [InlineData(Cli.OverlayOnRetina, Skip = "Waiting for its code")]
    public async Task TheCliAnswersOnMacOS(Cli row)
    {
        var cancel = TestContext.Current.CancellationToken;
        if (row == Cli.OverlayOnRetina)
        {
            await FromTheOverlayAsync(cancel);
            return;
        }

        // A shell, and ssh, start whatever they are told, so only a session made for tests runs them.
        await using var session = OwnSession.OnScreen();
        await SensorFeed.ServeAsync(session, SensorFeed.MacMini, cancel);

        // Each command by the name thermi-cli has on the PATH the cask links it to; over SSH, in a login of the person's
        // with no graphical session, which macOS gives no window server.
        foreach (var (args, schema, exit) in Commands)
        {
            var said = await RunAsync(session, row, $"thermi-cli {args}; echo \"ended $?\"", cancel);
            var lines = said.Out.TrimEnd().Split('\n');
            Assert.Equal($"ended {exit}", lines[^1]);
            var json = string.Join('\n', lines[..^1]);
            var document = json.Length == 0 ? null : JsonNode.Parse(json);
            Assert.Equal(exit == 0, document is not null);
            Assert.Empty(schema is null ? [] : SchemaCheck.Findings(AgentSchemas.Current[schema], document));
        }

        var monitors = JsonNode.Parse((await RunAsync(session, row, "thermi-cli monitors", cancel)).Out)!;
        Assert.Equal("overlay-not-running", NotMeasured(monitors));
        Assert.Equal(("running", false), await session.SdkStatusAsync(cancel));
        await StartsAtLoginAsync(session, row, cancel);
        await ReachesThePerformanceCoresAsync(session, row, cancel);
    }

    [Fact(Skip = "Waiting for its code", Timeout = Bound.Test)]
    public async Task AFileLosesNothingOnMacOS()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        await SensorFeed.ServeAsync(session, SensorFeed.MacMini, cancel);
        var settings = Path.Combine(session.Home, "Library", "Application Support", "thermi", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);

        // A file Windows wrote, with hotkeys, tray icons and the window's place, and one Linux wrote, each with FPS and frame
        // time items, which macOS does not read: an agent's edit writes them back with nothing lost.
        foreach (var name in new[] { "v6.json", "joined.json" })
        {
            var file = JsonNode.Parse(await File.ReadAllTextAsync(Built.Config(name), cancel))!;
            file.AsObject().Remove("agents");
            var items = file["overlays"]![0]!["items"]!.AsArray();
            items.Add(new JsonObject { ["id"] = Metrics.Fps.Id, ["metric"] = Metrics.Fps.Id });
            items.Add(new JsonObject { ["id"] = Metrics.FrameTime.Id, ["metric"] = Metrics.FrameTime.Id });
            await File.WriteAllTextAsync(settings, file.ToJsonString(), cancel);

            var edited = await session.RunAsync("thermi-cli", ["edit", """{"kind":"overlay","overlay":0,"field":"size","value":1.5}"""], cancel: cancel);
            var written = JsonNode.Parse(await File.ReadAllTextAsync(settings, cancel))!;
            var status = JsonNode.Parse((await session.RunAsync("thermi-cli", ["status", "--schema", "1"], cancel: cancel)).Out)!;

            Assert.Equal(0, edited.Exit);
            var expected = file.DeepClone();
            expected["overlays"]![0]!["size"] = 1.5;
            Assert.True(JsonNode.DeepEquals(expected, written), $"The file lost or changed what it held:\n{written}");
            Assert.Null(ThermiConfigJson.Read(written.ToJsonString()).Problem);
            Assert.Contains(status["problems"]!.AsArray(), problem => (string?)problem!["code"] == "frames-not-read");
        }

        // The FPS items take no room: the overlay paints the same with them and without them, which only thermi tells.
        await using var mac = await TestMac.StartAsync(null, cancel);
        await File.WriteAllTextAsync(mac.SettingsFile, await File.ReadAllTextAsync(settings, cancel), cancel);
        var with = await mac.PreviewAsync(SensorFeed.MacMini, 1, cancel);
        var without = await mac.ReadSettingsAsync(cancel);
        await mac.WriteSettingsAsync(
            without with { Overlays = [.. without.Overlays.Select(overlay => overlay with { Items = [.. overlay.Items.Where(Read)] })] }, cancel);
        Assert.Equal(0, (await mac.PreviewAsync(SensorFeed.MacMini, 1, cancel)).Differing(with));
    }

    // A command line in zsh or bash on the Mac, or in the person's login over SSH, which finds the build's programs and
    // reads the session's socket as the shell's own would; the login takes the session's home and temporary folder, where
    // an edit and a look write, never the person's.
    private static Task<Ran> RunAsync(OwnSession session, Cli row, string line, CancellationToken cancel) => row switch
    {
        Cli.OverSsh => session.RunAsync(
            "ssh",
            [
                "-o", "BatchMode=yes", "localhost",
                $"export HOME='{session.Home}' TMPDIR='{session.Temporary}' LANG=en_US.UTF-8 THERMI_SENSORS_SOCKET='{session.Socket}' "
                    + $"PATH='{AppContext.BaseDirectory}':$PATH; {line}",
            ],
            cancel: cancel),
        Cli.Bash => session.RunAsync("/bin/bash", ["-c", line], cancel: cancel),
        Cli.Zsh or Cli.OverlayOnRetina => session.RunAsync("/bin/zsh", ["-c", line], cancel: cancel),
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "Unknown shell."),
    };

    // start-with-windows keeps its name on the Mac and sets Open at login, the overlay's agent's start at login, without
    // starting the overlay.
    private static async Task StartsAtLoginAsync(OwnSession session, Cli row, CancellationToken cancel)
    {
        var agent = Path.Combine(session.Home, "Library", "LaunchAgents", $"{Launchd.OverlayLabel}.plist");
        var (gui, _) = await session.DomainsAsync(cancel);
        foreach (var on in new[] { true, false })
        {
            Assert.Equal(0, (await RunAsync(session, row, $"thermi-cli start-with-windows {(on ? "on" : "off")}", cancel)).Exit);
            Assert.Equal(on, (bool?)(await PackageTests.PlistAsync(session, agent, cancel))["RunAtLoad"] == true);
            Assert.DoesNotContain("pid = ", await Launchd.PrintAsync(session, gui, Launchd.OverlayLabel, cancel) ?? "", StringComparison.Ordinal);
        }
    }

    // find-cpu-fan's load takes more of the CPU than the efficiency cores alone give, where macOS keeps a background thread:
    // its CPU time over its run, as time says it, past a core for each of them; a Mac of one kind of core has none.
    private static async Task ReachesThePerformanceCoresAsync(OwnSession session, Cli row, CancellationToken cancel)
    {
        var line = "sysctl -n hw.perflevel1.logicalcpu 2>/dev/null || echo 0; /usr/bin/time -p thermi-cli find-cpu-fan 2>&1 >/dev/null";
        var said = (await RunAsync(session, row, line, cancel)).Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var efficiency = int.Parse(said[0], CultureInfo.InvariantCulture);
        Assert.True(
            (Seconds(said, "user") + Seconds(said, "sys")) / Seconds(said, "real") > efficiency,
            $"find-cpu-fan's load took no more than the efficiency cores: {string.Join(' ', said)}");
    }

    // A time time -p says, on its line of the name.
    private static double Seconds(string[] said, string name) =>
        double.Parse(said.Last(line => line.StartsWith($"{name} ", StringComparison.Ordinal))[(name.Length + 1)..], CultureInfo.InvariantCulture);

    private static bool Read(ItemConfig item) => item.Metric != Metrics.Fps.Id && item.Metric != Metrics.FrameTime.Id;

    // The monitors as the running overlay places its overlays in their visible frames, at a Retina scale.
    private static async Task FromTheOverlayAsync(CancellationToken cancel)
    {
        await using var mac = await TestMac.StartAsync(SensorFeed.MacMini, cancel);
        var main = (await mac.MonitorsAsync(cancel))[0];
        Assert.SkipWhen(main.Scale < 2, "The main monitor is not Retina.");
        await mac.WriteSettingsAsync(ThermiConfig.New, cancel);
        await mac.ShowThermiAsync([], cancel);

        var status = JsonNode.Parse((await mac.Session.RunAsync("thermi-cli", ["status", "--schema", "1"], cancel: cancel)).Out)!;
        var monitors = JsonNode.Parse((await mac.Session.RunAsync("thermi-cli", ["monitors"], cancel: cancel)).Out)!;

        Assert.True((bool)status["overlay"]!);
        Assert.Null(monitors["problem"]);
        var monitor = monitors["monitors"]!.AsArray()[0]!;
        Assert.Equal(
            (main.Visible.Width, main.Visible.Height, main.Scale),
            ((int)monitor["workArea"]!["width"]!, (int)monitor["workArea"]!["height"]!, (double)monitor["scale"]!));
    }

    // The problem monitors answers with no monitor, which only a running overlay measures.
    private static string? NotMeasured(JsonNode monitors)
    {
        Assert.Empty(monitors["monitors"]!.AsArray());
        return (string?)monitors["problem"]?["code"];
    }
}
