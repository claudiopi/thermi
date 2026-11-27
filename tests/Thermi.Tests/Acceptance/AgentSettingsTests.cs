using System.Text.Json.Nodes;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Cli.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Tests.Agents;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What an agent finds and changes as the settings window does (<c>docs/specs/003-agents/spec.md</c>): the sensors and the
/// settings, the saved looks, the monitors and a point on them, the CPU fan's finder and the start at sign-in.
/// </summary>
public sealed class AgentSettingsTests : IDisposable
{
    private readonly AgentRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private string Looks => ConfigStore.StylesFolder(_rig.Folder.File);

    [Fact]
    public async Task SensorsAndConfigAnswerAsTheWindow()
    {
        // With no file yet, the settings are those thermi starts with, which the first edit writes.
        var defaults = (await _rig.RunAsync("config")).Json["settings"];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ThermiConfigJson.Write(ThermiConfig.New)), defaults));

        var snapshot = TestRecordings.Read(TestRecordings.Admin);
        var config = OverlayEdits.Add(new ThermiConfig()).Config;
        _rig.Folder.Write(ThermiConfigJson.Write(config));

        var sensors = (await _rig.RunAsync("sensors", "--search", "cpu", "--type", "Temperature")).Json;
        var expected = AgentViews.Sensors(snapshot, config, "cpu", [SensorKind.Temperature]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(AgentJson.Write(expected)), sensors));
        Assert.NotEmpty(expected.Devices);
        Assert.All(expected.Devices.SelectMany(device => device.Sensors), sensor => Assert.Equal(SensorKind.Temperature, sensor.Type));

        var settings = (await _rig.RunAsync("config")).Json["settings"]!;
        Assert.Equal(2, settings["overlays"]!.AsArray().Count);
        Assert.Equal(
            config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Id),
            settings["overlays"]!.AsArray().SelectMany(overlay => overlay!["items"]!.AsArray()).Select(item => (string)item!["id"]!));
    }

    [Fact]
    public async Task AgentLooksAreTheWindowsLooks()
    {
        var saved = new SavedStyles(Looks);
        var config = OverlayEdits.Add(new ThermiConfig()).Config;
        config = StyleEdits.Restyle(config, 1, style => style with { Gap = 3 }).Config;
        _rig.Folder.Write(ThermiConfigJson.Write(config));
        Assert.Null(saved.Save(new StyleConfig(Gap: 12), "Calm"));
        Assert.Null(saved.Save(new StyleConfig(Gap: 10, Units: false), "Loud"));

        var looks = await _rig.RunAsync("looks");
        Assert.Equal((0, ""), (looks.Exit, looks.Error));
        Assert.Equal(["Calm", "Loud"], looks.Json["looks"]!.AsArray().Select(name => (string)name!));
        // A name that starts with @ is a name, never a file of arguments to read.
        Assert.Equal(0, (await _rig.RunAsync("look", "save", "@Mine", "--overlay", "1")).Exit);
        Assert.Equal(3, saved.Named("@Mine")!.Gap);

        Assert.Equal(0, (await _rig.RunAsync("look", "wear", "Calm")).Exit);
        config = StyleEdits.Wear(config, overlay: null, saved.Named("Calm")!).Config;
        Assert.Equal(ThermiConfigJson.Write(config), ThermiConfigJson.Write(_rig.Folder.Read()));

        using (File.Open(Path.Combine(Looks, "Calm" + StyleFile.Extension), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(4, (await _rig.RunAsync("look", "rename", "Calm", "Quiet")).Exit);
        }

        Assert.Equal(["@Mine", "Calm", "Loud"], saved.List().Select(style => style.Name));

        // A rename the settings cannot follow, the file read-only, gives the look its old name back, as it was written.
        File.SetAttributes(_rig.Folder.File, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(4, (await _rig.RunAsync("look", "rename", "calm", "Quiet")).Exit);
        }
        finally
        {
            File.SetAttributes(_rig.Folder.File, FileAttributes.Normal);
        }

        Assert.Equal(["@Mine", "Calm", "Loud"], saved.List().Select(style => style.Name));

        var taken = await _rig.RunAsync("look", "rename", "Calm", "Loud");
        Assert.Equal((1, Texts.Key(Refusal.StyleNameTaken)), (taken.Exit, (string)taken.Json["refusal"]!));

        var exported = (string)(await _rig.RunAsync("look", "export", "Loud")).Json["file"]!;
        Assert.Equal(StyleFile.Write(saved.Named("Loud")!), exported);
        Assert.Equal(0, (await _rig.RunAsync("look", "delete", "Loud")).Exit);
        Assert.Null(saved.Named("Loud"));

        Assert.Equal(0, (await _rig.RunAsync("look", "import", exported, "--overlay", "0")).Exit);
        config = StyleEdits.Import(config, 0, exported).Config;
        Assert.Equal(ThermiConfigJson.Write(config), ThermiConfigJson.Write(_rig.Folder.Read()));
        var broken = await _rig.RunAsync("look", "import", "{ not a look");
        Assert.Equal((1, Texts.Key(StyleEdits.Import(config, 0, "{ not a look").Refused!.Value)), (broken.Exit, (string)broken.Json["refusal"]!));

        // Beside the settings, only what they were before the agent's last change.
        Assert.Equal(["config.before-agent.json", "config.json"], _rig.Folder.Files.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AgentsPlaceOverlaysOnMonitors()
    {
        _rig.Screens =
        [
            new Screen("DISPLAY1", Primary: true, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), Scale: 1)
            {
                Taskbar = new PixelRect(0, 1032, 1920, 1080),
            },
            new Screen("DISPLAY2", Primary: false, new PixelRect(1920, 0, 4480, 1440), new PixelRect(1920, 0, 4480, 1440), Scale: 1.5),
        ];
        var config = OverlayEdits.Add(new ThermiConfig()).Config;
        _rig.Folder.Write(ThermiConfigJson.Write(config));

        var listed = await _rig.RunAsync("monitors");
        Assert.Equal((0, ""), (listed.Exit, listed.Error));
        var monitors = listed.Json["monitors"]!.AsArray();
        Assert.Equal(["DISPLAY1", "DISPLAY2"], monitors.Select(monitor => (string)monitor!["id"]!));
        Assert.Equal((2560, true, false), ((int)monitors[1]!["bounds"]!["width"]!, (bool)monitors[0]!["taskbar"]!, (bool)monitors[1]!["taskbar"]!));

        Assert.Equal(0, (await _rig.RunAsync("edit", """{"kind":"place","overlay":1,"x":2400,"y":300}""")).Exit);
        Assert.Equal(ThermiConfigJson.Write(OverlayEdits.Drop(config, 1, new Position(2400, 300)).Config), ThermiConfigJson.Write(_rig.Folder.Read()));
    }

    // The Intel desktop's board fan answers its load; the Ryzen's, its fans held where they were before it, answers nothing.
    [Theory]
    [InlineData(TestRecordings.IntelCpuLoad, true)]
    [InlineData(TestRecordings.CpuLoad, false)]
    public async Task AgentsFindTheCpuFan(string recording, bool answers)
    {
        var recorded = TestRecordings.Series(recording);
        // Its readings go on after the recording, for the fans to be watched once the load ends.
        var series = answers ? recorded
            :
            [
                .. recorded.Select(snapshot => Held(snapshot, recorded[0])),
                .. Enumerable.Repeat(Held(recorded[^1], recorded[0]), (int)FanFinder.Afterwards.TotalSeconds),
            ];
        _rig.Sensors = () => new RecordedFeed(series, pace: _rig.Folder.Clock);
        _rig.Folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var before = _rig.Folder.Text();

        var found = await _rig.RunAsync("find-cpu-fan", "--use");

        Assert.Equal(answers ? 0 : 1, found.Exit);

        // An agent's loop cannot keep the CPU busy: a run so soon after the last loads nothing and says when the next can.
        var resting = await _rig.RunAsync("find-cpu-fan");
        Assert.Equal((1, true, 1), (resting.Exit, (int?)resting.Json["runsAgainIn"] > 0, _rig.Loads.Count));
        _rig.Folder.Clock.Advance(TimeSpan.FromMinutes(2));

        // Nor can two runs at once: one another process runs holds the file, and this one rests.
        var stamp = Path.Combine(Path.GetDirectoryName(_rig.Folder.File)!, "fan-finder-ran.txt");
        using (File.Open(stamp, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var atOnce = await _rig.RunAsync("find-cpu-fan");
            Assert.Equal((1, true, 1), (atOnce.Exit, (int?)atOnce.Json["runsAgainIn"] > 0, _rig.Loads.Count));
        }

        if (answers)
        {
            // A PC whose board reports no fan: the finder cannot start, says why, and loads nothing.
            _rig.Sensors = () => new RecordedFeed([new Snapshot([.. series[0].Sensors.Where(sensor => sensor.Type != nameof(SensorKind.Fan))])]);
            var none = await _rig.RunAsync("find-cpu-fan");
            Assert.Equal((1, false), (none.Exit, none.Json["cannotStart"] is null));
            _rig.Sensors = () => new RecordedFeed(series, pace: _rig.Folder.Clock);
        }

        var (started, ended) = Assert.Single(_rig.Loads);
        Assert.InRange(_rig.Folder.Clock.GetElapsedTime(started, ended!.Value), TimeSpan.Zero, FanFinder.MaxRun);
        if (!answers)
        {
            Assert.NotEqual(nameof(FanFinderOutcome.Found), (string?)found.Json["outcome"], StringComparer.OrdinalIgnoreCase);
            Assert.Equal(before, _rig.Folder.Text());
            return;
        }

        var fan = (string)found.Json["fan"]!;
        // The fan is the CPU fan item's, as the window's Use it makes it.
        var used = ItemEdits.ChangeSensor(new ThermiConfig(), Metrics.CpuFan.Id, series[^1], fan).Config;
        Assert.Equal(ThermiConfigJson.Write(used), ThermiConfigJson.Write(_rig.Folder.Read()));

        // Settings with no CPU fan item to take it: the finder still says which fan it found, and why it was not used.
        var defaults = new ThermiConfig();
        var noFanItem = defaults.Overlays[0] with { Items = [.. defaults.Overlays[0].Items.Where(item => (item.Metric ?? item.Auto) != Metrics.CpuFan.Id)] };
        _rig.Folder.Write(ThermiConfigJson.Write(defaults with { Overlays = [noFanItem] }));
        var unused = await _rig.RunAsync("find-cpu-fan", "--use");
        Assert.Equal(
            (1, fan, false, Texts.Key(Refusal.NotFound)),
            (unused.Exit, (string?)unused.Json["fan"], (bool)unused.Json["used"]!, (string?)unused.Json["refusal"]));

        // The finder is a change as the others are: one called while it runs waits for it.
        _rig.Folder.Clock.Advance(TimeSpan.FromMinutes(2));
        using var commands = new AgentCommands(_rig.Environment, _rig.Folder.Clock);
        var finding = commands.RunAsync(new AgentRequest(AgentVerb.FindCpuFan), TestContext.Current.CancellationToken);
        var redo = commands.RedoAsync(TestContext.Current.CancellationToken);
        for (var step = 0; !redo.IsCompleted && step < 2000; step++)
        {
            _rig.Folder.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(TimeSpan.FromMilliseconds(2), TimeProvider.System, TestContext.Current.CancellationToken);
        }

        Assert.True(finding.IsCompleted);
        Assert.Equal(AgentExit.Done, (await finding).Exit);
    }

    private static Snapshot Held(Snapshot snapshot, Snapshot before) => snapshot with
    {
        Sensors = [.. snapshot.Sensors.Select(sensor => sensor.Type == nameof(SensorKind.Fan) ? before.Sensors.Single(each => each.Id == sensor.Id) : sensor)],
    };

    [Fact]
    public async Task AgentsSetStartWithWindows()
    {
        var steps = new[] { ("off", false), (null, false), ("on", true), (null, true) };
        foreach (var (said, on) in steps)
        {
            var end = await _rig.RunAsync(said is null ? ["start-with-windows"] : ["start-with-windows", said]);
            Assert.Equal((0, on, on), (end.Exit, (bool)end.Json["on"]!, _rig.Startup.On));
        }

        _rig.SensorsOnly = true;
        var refused = await _rig.RunAsync("start-with-windows", "off");
        Assert.Equal((1, false, true), (refused.Exit, (bool)refused.Json["overlay"]!, _rig.Startup.On));
    }
}
