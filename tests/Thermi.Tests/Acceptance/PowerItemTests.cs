using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Combined sensors, min, max and average, smoothing, graphs, decimals, alerts, overlays and OLED care.
/// </summary>
public sealed class PowerItemTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Ccds = ["/amdcpu/0/temperature/3", "/amdcpu/0/temperature/4"];

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static OverlayConfig Temperature(Func<ItemConfig, ItemConfig> change) =>
        new() { Items = [change(new ItemConfig { Id = Metrics.CpuTemperature.Id, Metric = Metrics.CpuTemperature.Id })] };

    // What the item shows as each value arrives, a second apart on the fake clock.
    private static List<string> Feed(ItemHistory history, FakeTimeProvider time, OverlayConfig overlay, params double[] values)
    {
        var shown = new List<string>();
        foreach (var value in values)
        {
            var readings = new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, value) };
            history.Add(overlay, readings, null);
            shown.Add(OverlayLayout.Build(overlay, readings, English, history: history).Single().Value);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        return shown;
    }

    [Fact]
    public void AnItemCombinesSensorsOfOneType()
    {
        var empty = new ThermiConfig { Overlays = [new OverlayConfig { Items = [] }] };
        var cores = Enumerable.Range(1, 16).Select(core => "/amdcpu/0/power/" + core.ToString(CultureInfo.InvariantCulture)).ToList();
        var highest = Done(PowerEdits.AddCombined(empty, 0, Desktop, Ccds, Combine.Max));
        var average = Done(PowerEdits.AddCombined(highest, 0, Desktop, [.. Ccds, Ccds[0]], Combine.Average));
        var three = Done(PowerEdits.AddCombined(average, 0, Desktop, cores, Combine.Sum));
        var ids = Ids(three.Overlays[0]);
        var offset = Done(PowerEdits.SetOffset(Done(PowerEdits.SetOffset(three, ids[0], Desktop, -5)), ids[1], Desktop, -5));
        var overlay = ThermiConfigJson.Read(File(offset)).Config.Overlays[0];
        var unread = new Snapshot([.. Desktop.Sensors.Select(sensor => sensor with { Value = double.NaN })]);

        var shown = OverlayLayout.Build(overlay, SensorMap.Read(Desktop, overlay), English);
        var inFahrenheit = OverlayLayout.Build(overlay, SensorMap.Read(Desktop, overlay), English, temperature: TemperatureUnit.Fahrenheit);

        Assert.Equal(["67 °C", "54 °C", "27 W"], shown.Select(item => item.Value));
        Assert.Equal(["152 °F", "130 °F"], inFahrenheit.Take(2).Select(item => item.Value));
        Assert.Equal(-9, Temperatures.ShownDifference(-5, TemperatureUnit.Fahrenheit));
        Assert.Equal(Ccds, Item(offset, ids[1]).Sensors);
        Assert.All(OverlayLayout.Build(overlay, SensorMap.Read(unread, overlay), English), item => Assert.Equal(ReadingFormatter.Missing, item.Value));
        Assert.Equal(ReadingFormatter.Missing, OverlayLayout.Build(overlay, SensorMap.Read(new Snapshot([]), overlay), English)[0].Value);
        var edits = CombinedEdits(empty, three, cores);
        Assert.Equal(edits.Select(row => (row.Edit, row.Refused)), edits.Select(row => (row.Edit, row.Result.Refused)));
        Assert.Equal(Combine.Average, Item(Done(PowerEdits.SetCombine(three, ids[0], Desktop, Combine.Average)), ids[0]).Combine);
        var power = Item(Done(PowerEdits.SetSensors(new ThermiConfig(), Metrics.CpuPower.Id, Desktop, [cores[0], cores[1], cores[0]])), Metrics.CpuPower.Id);
        Assert.Equal([cores[0], cores[1]], power.Sensors);
        Assert.Equal((Combine.Max, null, Metrics.CpuPower.Id), (power.Combine, power.Metric, power.Auto));

        // A file is held to the same range, -20 to 20 for a load: past it the whole file is broken.
        Assert.Equal<ConfigProblemKind?>(
            [null, ConfigProblemKind.Broken],
            new[] { 20, 30 }.Select(offset => ThermiConfigJson
                .Read(File(WithItem(new ThermiConfig(), Metrics.CpuLoad.Id, item => item with { Offset = offset }))).Problem?.Kind));
    }

    // Each edit of a combined item, named, with what it is refused for, or null when it passes; a failure names its row.
    private static (string Edit, EditResult Result, Refusal? Refused)[] CombinedEdits(ThermiConfig empty, ThermiConfig three, List<string> cores)
    {
        var ids = Ids(three.Overlays[0]);
        return
        [
            ("a sum of temperatures added", PowerEdits.AddCombined(empty, 0, Desktop, Ccds, Combine.Sum), Refusal.SumOfTemperatures),
            ("a sum of temperatures set", PowerEdits.SetCombine(three, ids[0], Desktop, Combine.Sum), Refusal.SumOfTemperatures),
            ("a sum of sensors that are gone", PowerEdits.SetCombine(three, ids[2], new Snapshot([]), Combine.Sum), Refusal.SumOfUnknown),
            ("one sensor twice", PowerEdits.AddCombined(empty, 0, Desktop, [Ccds[0], Ccds[0]], Combine.Max), Refusal.CombineNeedsTwo),
            ("a combine on a single metric", PowerEdits.SetCombine(new ThermiConfig(), Metrics.CpuLoad.Id, Desktop, Combine.Max), Refusal.CombineNeedsTwo),
            ("a temperature with a power", PowerEdits.AddCombined(empty, 0, Desktop, [Ccds[0], cores[0]], Combine.Max), Refusal.SensorOfAnotherType),
            ("a sensor that is gone", PowerEdits.AddCombined(empty, 0, Desktop, [Ccds[0], "/gone/0"], Combine.Max), Refusal.SensorNotFound),
            ("an overlay that is not there", PowerEdits.AddCombined(empty, 1, Desktop, Ccds, Combine.Max), Refusal.NotFound),
            ("sensors set to one", PowerEdits.SetSensors(new ThermiConfig(), Metrics.CpuPower.Id, Desktop, [cores[0]]), Refusal.CombineNeedsTwo),
            (
                "sensors set to a power and a temperature",
                PowerEdits.SetSensors(new ThermiConfig(), Metrics.CpuPower.Id, Desktop, [cores[0], Ccds[0]]),
                Refusal.SensorOfAnotherType),
            ("sensors set on an item that is gone", PowerEdits.SetSensors(new ThermiConfig(), "gone", Desktop, [cores[0], cores[1]]), Refusal.NotFound),
            ("a temperature's offset under -20", PowerEdits.SetOffset(three, ids[0], Desktop, -21), Refusal.OffsetOutOfRange),
            ("a power's offset over 500", PowerEdits.SetOffset(three, ids[2], Desktop, 501), Refusal.OffsetOutOfRange),
            ("a power's offset of 500", PowerEdits.SetOffset(three, ids[2], Desktop, 500), null),
            ("an offset on sensors that are gone", PowerEdits.SetOffset(three, ids[2], new Snapshot([]), 1), Refusal.SensorNotFound),
            ("an offset on an item that is gone", PowerEdits.SetOffset(three, "gone", Desktop, 1), Refusal.NotFound),
        ];
    }

    [Fact]
    public void AnItemShowsItsMaximumUntilReset()
    {
        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        var overlay = Temperature(item => item with { Stat = ItemStat.Max });
        var sent = 0;
        history.WasReset += (_, _) => sent++;

        var before = Feed(history, time, overlay, 60, 75, 70);

        // The item now behind another overlay, as after a merge or a move: its maximum goes with it, and only its own reset clears it.
        var other = new OverlayConfig { Items = [new ItemConfig { Id = Metrics.GpuLoad.Id, Metric = Metrics.GpuLoad.Id }] };
        var cooler = new Dictionary<string, Reading>(TrayTextTests.Quiet, StringComparer.Ordinal)
        {
            [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 65),
        };
        history.Add([(other, TrayTextTests.Quiet), (overlay, cooler)], null);
        history.Reset([other]);
        var moved = OverlayLayout.Build(overlay, cooler, English, history: history).Single().Value;
        time.Advance(TimeSpan.FromSeconds(1));
        history.Reset([other, overlay]);
        var after = Feed(history, time, overlay, 65, 62);

        Assert.Equal(["60 °C", "75 °C", "75 °C"], before);
        Assert.Equal("75 °C", moved);
        Assert.Equal(["65 °C", "65 °C"], after);

        // An open window is sent the whole history once a reset, however many overlays it starts again.
        Assert.Equal(2, sent);
    }

    // NaN for a value the sensor did not give; what shows when nothing is left to average is the missing value.
    [Theory] // Rows: nothing left to average shows the missing value.
    [InlineData("60 90 60 30", "60 °C|75 °C|70 °C|60 °C")]
    [InlineData("60 NaN NaN NaN", "60 °C|60 °C|60 °C|" + ReadingFormatter.Missing)]
    public void SmoothingAveragesTheLastSeconds(string values, string expected)
    {
        var time = new FakeTimeProvider();

        var shown = Feed(
            new ItemHistory(time),
            time,
            Temperature(item => item with { Smooth = 3 }),
            [.. values.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture))]);

        Assert.Equal(expected, string.Join('|', shown));
        Assert.Equal(Refusal.Invalid, PowerEdits.SetSmooth(new ThermiConfig(), Metrics.CpuLoad.Id, 1).Refused);
        Assert.Equal(10, Item(Done(PowerEdits.SetSmooth(new ThermiConfig(), Metrics.CpuLoad.Id, 10)), Metrics.CpuLoad.Id).Smooth);
    }

    [Fact]
    public void AGraphMeterShowsTheLastMinute()
    {
        var time = new FakeTimeProvider();
        var history = new ItemHistory(time);
        var lined = Done(ItemEdits.SetThreshold(new ThermiConfig(), Metrics.GpuLoad.Id, 60, 80));
        var config = Done(PowerEdits.SetGraph(lined, Metrics.GpuLoad.Id, GraphStyle.Line));
        var overlay = Done(PowerEdits.SetGraph(config, Metrics.CpuFan.Id, GraphStyle.Line)).Overlays[0];
        IReadOnlyList<OverlayItem> shown = [];

        for (var second = 0; second < 70; second++)
        {
            var readings = new Dictionary<string, Reading>(TrayTextTests.Quiet, StringComparer.Ordinal)
            {
                [Metrics.GpuLoad.Id] = new(SensorKind.Load, second % 7 == 0 ? double.NaN : second % 10 * 10),
                [Metrics.CpuFan.Id] = new(SensorKind.Fan, 1000),
            };
            history.Add(overlay, readings, null);
            shown = OverlayLayout.Build(overlay, readings, English, history: history);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        var graph = shown.Single(item => item.Label == "GPU").Graph!;
        Assert.Equal(60, graph.Count);
        Assert.Equal(new GraphPoint(0.1, Level.Normal), graph[1]);
        Assert.Equal(new GraphPoint(0.7, Level.Warning), graph[7]);
        Assert.Equal(new GraphPoint(0.9, Level.Critical), graph[9]);
        Assert.Equal([4, 11, 18, 25, 32, 39, 46, 53], graph.Select((point, at) => (point, at)).Where(each => each.point is null).Select(each => each.at));
        // A fan's line reads on the scale the window's chart gives the same readings, 0 to 1,500 RPM, not as a load: the
        // night QA saw 1,000 RPM pinned at the top, and then nearly flat on a scale to 10,000.
        Assert.Equal(new GraphPoint(1000.0 / 1500, Level.Normal), shown.Single(item => item.Id == Metrics.CpuFan.Id).Graph![^1]);
        Assert.Equal(
            [new GraphPoint(0.03, Level.Normal)],
            OverlayLayout.Build(overlay, TrayTextTests.Quiet, English).Single(item => item.Label == "GPU").Graph);
        Assert.Equal(Refusal.JudgingNeeded, PowerEdits.SetGraph(config, Metrics.CpuPower.Id, GraphStyle.Line).Refused);
        var ramBars = Done(PowerEdits.SetGraph(config, Metrics.RamUsed.Id, GraphStyle.Bars));
        Assert.Null(Item(Done(ItemEdits.SetWarnBy(ramBars, Metrics.RamUsed.Id, Desktop, null)), Metrics.RamUsed.Id).Graph);

        history.Add(overlay with { Items = [.. overlay.Items.Where(item => item.Id != Metrics.GpuLoad.Id)] }, TrayTextTests.Quiet, null);
        Assert.Equal(Enumerable.Repeat<GraphPoint?>(null, 60), history.Graph(overlay.Items.Single(item => item.Id == Metrics.GpuLoad.Id)));
    }

    [Fact]
    public void DecimalsChangeTheValueAndItsRoom()
    {
        var ram = new Reading(SensorKind.Data, 27.34);
        var config = Done(PowerEdits.SetDecimals(new ThermiConfig(), Metrics.RamUsed.Id, 2));

        var shown = OverlayLayout.Build(config.Overlays[0], TrayTextTests.Quiet, English).Single(item => item.Label == "RAM");

        Assert.Equal(["27 GB", "27.3 GB", "27.34 GB"], new[] { 0, 1, 2 }.Select(decimals => ReadingFormatter.Format(ram, English, decimals: decimals)));
        Assert.Equal(
            ["889 GB", "888.8 GB", "888.80 GB"],
            new[] { 0, 1, 2 }.Select(decimals => ReadingFormatter.Widest(SensorKind.Data, English, decimals: decimals)));
        Assert.Equal(("27.30 GB", "888.80 GB"), (shown.Value, shown.Room));
        Assert.Equal("1,087.0 RPM", ReadingFormatter.Format(new Reading(SensorKind.Fan, 1087), English, decimals: 1));
        Assert.Equal(
            ["27.3", "62.0", "62"],
            new[] { (27.34, 2), (62, 1), (62, 0) }
                .Select(each => TrayTexts.IconText(new Reading(SensorKind.Data, each.Item1), English, decimals: each.Item2)));
        Assert.Equal(Refusal.Invalid, PowerEdits.SetDecimals(config, Metrics.RamUsed.Id, 3).Refused);
        Assert.Null(Item(Done(PowerEdits.SetDecimals(config, Metrics.RamUsed.Id, null)), Metrics.RamUsed.Id).Decimals);
    }

    [Fact]
    public void AnAlertOnlyItemShowsOnlyAtWarning()
    {
        var lined = Done(ItemEdits.SetThreshold(new ThermiConfig(), Metrics.GpuLoad.Id, 60, 80));
        var config = Done(PowerEdits.SetAlertOnly(lined, Metrics.GpuLoad.Id, true));
        var overlay = config.Overlays[0];

        var calm = OverlayLayout.Build(overlay, WithGpu(40), English);
        var hot = OverlayLayout.Build(overlay, WithGpu(70), English);
        var open = OverlayLayout.Build(overlay, WithGpu(40), English, windowOpen: true);

        Assert.Equal(["CPU", "RAM"], calm.Select(item => item.Label).OfType<string>());
        Assert.Equal(Level.Warning, hot.Single(item => item.Label == "GPU").Level);
        Assert.True(Width(overlay, calm) < Width(overlay, hot));
        Assert.Equal(RightEdge(overlay, hot), RightEdge(overlay, calm));
        Assert.Contains(open, item => item.Label == "GPU");
        Assert.Equal(Refusal.JudgingNeeded, PowerEdits.SetAlertOnly(new ThermiConfig(), Metrics.CpuPower.Id, true).Refused);
        Assert.Equal(Refusal.JudgingNeeded, PowerEdits.SetNotify(new ThermiConfig(), Metrics.CpuPower.Id, true).Refused);
        Assert.True(Item(Done(PowerEdits.SetNotify(lined, Metrics.GpuLoad.Id, true)), Metrics.GpuLoad.Id).Notify);
        Assert.False(Item(Done(PowerEdits.SetAlertOnly(new ThermiConfig(), Metrics.CpuPower.Id, false)), Metrics.CpuPower.Id).AlertOnly);
        var ramAlert = Done(PowerEdits.SetAlertOnly(new ThermiConfig(), Metrics.RamUsed.Id, true));
        Assert.False(Item(Done(ItemEdits.SetWarnBy(ramAlert, Metrics.RamUsed.Id, Desktop, null)), Metrics.RamUsed.Id).AlertOnly);
        Assert.Contains(
            "alertOnly goes only with a reading lines judge",
            ThermiConfigJson.Read(File(WithItem(new ThermiConfig(), Metrics.CpuPower.Id, item => item with { AlertOnly = true }))).Problem?.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheStyleSetsFontCornersGapAndEffect()
    {
        var large = Done(StyleEdits.SetPreset(new ThermiConfig(), 0, StylePreset.Large));

        var fonts = Enum.GetValues<StyleFont>().Select(font => Restyled(large, style => style with { Font = font })).ToList();
        var corners = Enum.GetValues<CornerStyle>().Select(corner => Restyled(large, style => style with { Corners = corner })).ToList();
        var gaps = new[] { 0, 16 }.Select(gap => Restyled(large, style => style with { Gap = gap })).ToList();
        var effects = Enum.GetValues<TextEffect>().Select(effect => Restyled(large, style => style with { Effect = effect })).ToList();
        var faded = Restyled(large, style => style with { Opacity = new OpacityConfig(0.29) });

        Assert.Equal(Enum.GetValues<StyleFont>(), fonts.Select(config => Look(config).Font));
        Assert.Equal([10, 5, 0], corners.Select(config => Look(config).Radius(20)));
        Assert.Equal([100, 132], gaps.Select(config => Look(config).Arrange([50, 50], 20, 2).Width));
        Assert.Equal([TextEffect.None, TextEffect.Shadow, TextEffect.Outline, TextEffect.None], effects.Select(config => Look(config).Effect));
        Assert.Equal(
            (TextEffect.Shadow, TextEffect.Outline),
            (Look(faded).Effect, OverlayLook.Of(faded.Overlays[0].Style with { Effect = TextEffect.Outline }, false).Effect));
        Assert.Equal((1.0, PillColors.Dark.Outer), (OverlayLook.OutlineWidth, Look(effects[2]).OutlineColor));
        Assert.All([.. fonts, .. corners, .. gaps, .. effects, faded], config => Assert.Equal(StylePreset.Large, config.Overlays[0].Style.Preset));
        Assert.Equal(Refusal.Invalid, StyleEdits.Restyle(large, 0, style => style with { Gap = 17 }).Refused);
    }

    [Fact]
    public void AFileHoldsUpToFourOverlays()
    {
        var (config, problem) = ThermiConfigJson.Read(TestFiles.Config("v6.json"));
        var twoAlike = ThermiConfigJson.Read("{ \"version\": 2, \"overlays\": [ {}, {} ] }").Config;
        var (_, fiveProblem) = ThermiConfigJson.Read("{ \"version\": 2, \"overlays\": [ {}, {}, {}, {}, {} ] }");

        Assert.Null(problem);
        var (first, second) = (config.Overlays[0], config.Overlays[1]);
        Assert.Equal(["fan.cpu", "cpu.temperature", "ccd", "/gpu-nvidia/0/power/0"], first.Items.Select(item => item.Id));
        Assert.Equal(["cpu.temperature-2", "ram.used"], second.Items.Select(item => item.Id));
        Assert.Equal(new SnapConfig(SnapSpot.Taskbar, "DEL-41A8-3132344A") { Offset = 1652 }, first.Snapped);
        Assert.Null(second.Snapped);
        Assert.Equal(new Position(-1200, 1402), second.Position);
        Assert.Equal((StylePreset.Custom, StylePreset.Compact), (first.Style.Preset, second.Style.Preset));
        Assert.Equal(
            [.. OverlayConfig.DefaultItems.Select(item => item.Id), .. OverlayConfig.DefaultItems.Select(item => item.Id + "-2")],
            twoAlike.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Id));
        Assert.Contains("overlays holds 5", fiveProblem?.Reason, StringComparison.Ordinal);
        Assert.Single(ThermiConfigJson.Read(TestFiles.Config("v1.json")).Config.Overlays);
    }

    // Snapped to a corner, the square goes away from the corner's edges; on the taskbar, or dropped anywhere, it goes up
    // and left.
    [Theory] // Rows: each an operand of the spot's pattern.
    [InlineData(SnapSpot.BottomRight)]
    [InlineData(SnapSpot.Taskbar)]
    [InlineData(null)]
    public void OledCareMovesTheOverlayAroundASmallSquare(SnapSpot? spot)
    {
        var overlay = spot is { } snapped
            ? new OverlayConfig { Oled = true, Snap = new SnapConfig(snapped) }
            : new OverlayConfig { Oled = true, Position = new Position(100, 200) };
        var written = ThermiConfigJson.Write(new ThermiConfig { Overlays = [overlay] });
        var home = Snaps.Locate(overlay with { Oled = false }, [SnapTests.Main], 300, 54)!;

        var moves = Moves(overlay, [SnapTests.Main]);

        // A position dropped on the screen is home itself.
        Assert.Equal(overlay.Position ?? home, home);
        Assert.Equal([(0, 0), (-1, 0), (-1, -1), (0, -1), (0, 0)], moves.Select(at => (at.X - home.X, at.Y - home.Y)));
        Assert.Equal(home, Snaps.Locate(overlay, [SnapTests.Main], 300, 54, OledCare.Step - TimeSpan.FromSeconds(1)));
        Assert.Equal(home, Snaps.Locate(overlay with { Oled = false }, [SnapTests.Main], 300, 54, OledCare.Step));
        Assert.Equal(written, ThermiConfigJson.Write(new ThermiConfig { Overlays = [overlay] }));
        Assert.Equal(
            [(1, 1), (-1, 1), (1, -1), (-1, -1)],
            new SnapSpot[] { SnapSpot.TopLeft, SnapSpot.TopRight, SnapSpot.BottomLeft, SnapSpot.Taskbar }
                .Select(each => OledCare.Shift(each, OledCare.Step * 2)));
    }

    [Fact]
    public void AColorblindSafeSetIsOneClickAway()
    {
        var chosen = Done(StyleEdits.UseColorblindSafe(Done(StyleEdits.SetColor(new ThermiConfig(), 0, StyleColor.Label, "#C0C0C0")), 0));
        var style = ThermiConfigJson.Read(File(chosen)).Config.Overlays[0].Style;
        var look = OverlayLook.Of(style, lightTaskbar: false);
        var system = new HighContrastColors(Rgb.Parse("#000000"), Rgb.Parse("#FFFFFF"), Rgb.Parse("#1AEBFF"), Rgb.Parse("#FFFF00"));
        var contrast = OverlayLook.Of(style, lightTaskbar: false, system);
        Level[] levels = [Level.Normal, Level.Warning, Level.Critical];

        Assert.Equal(new StyleColors { Normal = "#0072B2", Warning = "#E69F00", Critical = "#D55E00", Label = "#C0C0C0" }, style.Colors);
        Assert.Equal(["#0072B2", "#E69F00", "#D55E00"], levels.Select(level => look.MeterColor(level).ToString()));
        Assert.Equal((system.Window, system.WindowText), (contrast.Pills.Outer, contrast.Pills.Border));
        Assert.Equal(
            [system.WindowText, system.WindowText, system.WindowText],
            [
                contrast.LabelColor.Color,
                contrast.ValueColor.Color,
                contrast.ValueColorOf(new OverlayItem("CPU", "62 °C", Level.Normal, "100 °C") { Color = "#FF0000" }).Color,
            ]);
        Assert.Equal([system.WindowText, system.Highlight, system.HotTrack], levels.Select(contrast.MeterColor));
        Assert.True(contrast.HighContrast);
        Assert.False(look.HighContrast);
    }

    // What the item shows as each value arrives, a second apart, NaN for one the sensor did not give; a history that has
    // no value yet, as one just reset, shows the value now.
    [Theory]
    [InlineData(ItemStat.Average, "60 NaN 80", "60 °C|60 °C|70 °C")]
    [InlineData(ItemStat.Min, "65 80 NaN 90", "65 °C|65 °C|65 °C|65 °C")]
    [InlineData(ItemStat.Max, "60 NaN 75 70", "60 °C|60 °C|75 °C|75 °C")]
    public void MinMaxAndAverageSkipMissingValues(ItemStat stat, string values, string expected)
    {
        var time = new FakeTimeProvider();
        var overlay = Temperature(item => item with { Stat = stat });
        var now = new Dictionary<string, Reading>(StringComparer.Ordinal) { [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, 90) };

        var shown = Feed(new ItemHistory(time), time, overlay, [.. values.Split(' ').Select(value => double.Parse(value, CultureInfo.InvariantCulture))]);
        var fresh = OverlayLayout.Build(overlay, now, English, history: new ItemHistory(time), windowOpen: true).Single();

        Assert.Equal(expected, string.Join('|', shown));
        Assert.Equal("90 °C", fresh.Value);
        Assert.Equal(stat, Item(Done(PowerEdits.SetStat(new ThermiConfig(), Metrics.CpuLoad.Id, stat)), Metrics.CpuLoad.Id).Stat);
    }

    private static Dictionary<string, Reading> WithGpu(double load) =>
        new(TrayTextTests.Quiet, StringComparer.Ordinal) { [Metrics.GpuLoad.Id] = new(SensorKind.Load, load) };

    // Pills of 60 px an item, 36 px tall at 150 %.
    private static int Width(OverlayConfig overlay, IReadOnlyList<OverlayItem> shown) =>
        (int)OverlayLook.Of(overlay.Style, lightTaskbar: false).Arrange([.. OverlayLayout.Groups(shown).Select(pill => 60.0 * pill.Count)], 36, 1.5).Width;

    private static int RightEdge(OverlayConfig overlay, IReadOnlyList<OverlayItem> shown) =>
        Snaps.Locate(overlay, [SnapTests.Main], Width(overlay, shown), 54)!.X + Width(overlay, shown);

    private static ThermiConfig Restyled(ThermiConfig config, Func<StyleConfig, StyleConfig> change) => Done(StyleEdits.Restyle(config, 0, change));

    private static OverlayLook Look(ThermiConfig config) => OverlayLook.Of(config.Overlays[0].Style, lightTaskbar: false);

    // Where the overlay is every 3 minutes for 12, on a fake clock from its start.
    private static List<Position> Moves(OverlayConfig overlay, IReadOnlyList<Screen> screens)
    {
        var time = new FakeTimeProvider();
        var start = time.GetTimestamp();
        var places = new List<Position>();
        for (var step = 0; step <= 4; step++)
        {
            places.Add(Snaps.Locate(overlay, screens, 300, 54, time.GetElapsedTime(start))!);
            time.Advance(OledCare.Step);
        }

        return places;
    }
}
