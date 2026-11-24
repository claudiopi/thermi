using System.Globalization;
using System.Text.Json.Nodes;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Every edit returns valid settings, or the reason it cannot.
/// </summary>
public sealed class EditTests
{
    private const string FanTwo = "/lpc/it8689e/0/fan/1";
    private const string FanTwoDuty = "/lpc/it8689e/0/control/1";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    [Fact]
    public void MovingAnItemReordersTheOverlay()
    {
        var moved = Done(ItemEdits.Move(new ThermiConfig(), Metrics.RamUsed.Id, 4));

        var groups = OverlayLayout.Groups(Shown(moved, Desktop));

        Assert.Equal(["CPU", "RAM", "GPU"], groups.Select(group => group[0].Label));
        Assert.Equal(["cpu.temperature", "fan.cpu", "cpu.load", "cpu.power", "ram.used", "gpu.load"], Ids(moved.Overlays[0]));
    }

    [Fact]
    public void AnyRecordedSensorCanBeAnItemInItsOwnUnit()
    {
        var units = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Temperature"] = " °C",
            ["Load"] = " %",
            ["Control"] = " %",
            ["Clock"] = " GHz",
            ["Power"] = " W",
            ["Fan"] = " RPM",
            ["Data"] = " GB",
        };

        // The recording has two sensors under one id, as the library reports them; an item names the first.
        foreach (var sensor in Desktop.Sensors.DistinctBy(sensor => sensor.Id, StringComparer.Ordinal))
        {
            var shown = Shown(Done(ItemEdits.AddSensor(new ThermiConfig(), 0, Desktop, sensor.Id)), Desktop);

            var item = Assert.Single(shown, each => each.Id == sensor.Id);
            Assert.EndsWith(units[sensor.Type], item.Room, StringComparison.Ordinal);
            Assert.True(item.Value == ReadingFormatter.Missing ? sensor.Value is null : item.Value.EndsWith(units[sensor.Type], StringComparison.Ordinal), $"{sensor.Id} shows {item.Value}");
        }

        // A GPU's sensor joins the GPU pill after its last item; the board's, whose kind has no pill, goes at the end.
        var gpu = Done(ItemEdits.AddSensor(new ThermiConfig(), 0, Desktop, "/gpu-nvidia/0/temperature/0"));
        var board = Done(ItemEdits.AddSensor(gpu, 0, Desktop, "/lpc/it8689e/0/temperature/0"));
        var again = Done(ItemEdits.AddSensor(board, 0, Desktop, "/gpu-nvidia/0/temperature/0"));
        Assert.Equal(["cpu.temperature", "fan.cpu", "cpu.load", "cpu.power", "gpu.load", "/gpu-nvidia/0/temperature/0", "ram.used", "/lpc/it8689e/0/temperature/0"], Ids(board.Overlays[0]));

        // Named like the defaults; the same sensor added again takes a number, so no two names are alike.
        var names = JsonNode.Parse(File(again))!["overlays"]![0]!["items"]!.AsArray().Select(each => each!["name"]?.GetValue<string>()).ToList();
        Assert.Equal(Texts.English.Of(MetricName.GpuTemperature), names[5]);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        NetworkDiskAndBatteryItemsTakeTheirLabels();
    }

    // Download and upload share one NET pill, each value after its arrow; read and write take labels of their own; a
    // battery's items take BAT and start with no lines, its rate signed and in tenths. The default overlay has none of them.
    private static void NetworkDiskAndBatteryItemsTakeTheirLabels()
    {
        var snapshot = new Snapshot([.. MadeUpHardware.Desktop().Sensors, .. MadeUpHardware.Laptop().Sensors.Where(sensor => sensor.HardwareType == "Battery")]);
        string[] added =
        [
            $"{MadeUpHardware.Ethernet}/throughput/8", $"{MadeUpHardware.Samsung}/throughput/54", $"{MadeUpHardware.Ethernet}/throughput/7",
            $"{MadeUpHardware.Samsung}/throughput/55", $"{MadeUpHardware.Battery}/level/0", $"{MadeUpHardware.Battery}/power/0", $"{MadeUpHardware.Battery}/timespan/0",
        ];

        var config = added.Aggregate(new ThermiConfig(), (current, id) => Done(ItemEdits.AddSensor(current, 0, snapshot, id)));
        var charge = Item(Done(ItemEdits.MeterItself(config, added[4], snapshot)), added[4]);

        var (net, read, write, bat) = (Texts.English.Of(MetricLabel.Net), Texts.English.Of(MetricLabel.Read), Texts.English.Of(MetricLabel.Write), Texts.English.Of(MetricLabel.Bat));
        Assert.Equal(
            [$"{net} ↓ 74.0 MB/s ↑ 1.45 MB/s", $"{read} 880 KB/s", $"{write} 54.9 MB/s", $"{bat} 88 % -12.4 W 2 h 14 min"],
            TrayTextTests.Lines(Shown(config, snapshot)).Skip(3));
        Assert.Equal(new MeterConfig { Sensor = added[4] }, charge.Meter);
        Assert.Null(charge.Threshold);
        Assert.Equal(["cpu.temperature", "fan.cpu", "cpu.load", "cpu.power", "gpu.load", "ram.used"], Ids(new ThermiConfig().Overlays[0]));
    }

    [Fact]
    public void AnItemsSensorCanOnlyChangeToOneOfTheSameType()
    {
        var config = new ThermiConfig();

        var toAFan = ItemEdits.ChangeSensor(config, Metrics.CpuTemperature.Id, Desktop, "/lpc/it8689e/0/fan/0");
        var toAnotherTemperature = ItemEdits.ChangeSensor(config, Metrics.CpuTemperature.Id, Desktop, "/amdcpu/0/temperature/3");

        Assert.Equal(Refusal.SensorOfAnotherType, toAFan.Refused);
        Assert.Same(config, toAFan.Config);

        // With its own sensor gone, its type is not known, so no sensor can be known to share it.
        var ccd = Done(ItemEdits.AddSensor(config, 0, Desktop, "/amdcpu/0/temperature/3"));
        var ccdGone = new Snapshot([.. Desktop.Sensors.Where(sensor => sensor.Id != "/amdcpu/0/temperature/3")]);
        Assert.Equal(Refusal.TypeUnknown, ItemEdits.ChangeSensor(ccd, "/amdcpu/0/temperature/3", ccdGone, "/lpc/it8689e/0/fan/0").Refused);
        Assert.Equal(Refusal.TypeUnknown, ItemEdits.ChangeSensor(ccd, "/amdcpu/0/temperature/3", ccdGone, "/amdcpu/0/temperature/4").Refused);
        Assert.Equal(
            Refusal.SensorOfAnotherType,
            ItemEdits.ChangeSensor(Done(PowerEdits.AddCombined(config, 0, Desktop, ["/amdcpu/0/temperature/3", "/amdcpu/0/temperature/4"], Combine.Max)), "/amdcpu/0/temperature/3", ccdGone, "/lpc/it8689e/0/fan/0").Refused);
        Assert.Equal(
            Item(config, Metrics.CpuTemperature.Id) with { Metric = null, Sensor = "/amdcpu/0/temperature/3", Auto = Metrics.CpuTemperature.Id },
            Item(Done(toAnotherTemperature), Metrics.CpuTemperature.Id));
    }

    // A fan metered by itself meters its new fan; one metered by a duty takes the new fan's own.
    [Fact]
    public void ChangingTheFanFollowsItsOwnDuty()
    {
        var desktop = new Snapshot([.. Desktop.Sensors.Select(sensor => sensor.Id == FanTwoDuty ? sensor with { Value = 40 } : sensor)]);
        var byDuty = WithItem(new ThermiConfig(), Metrics.CpuFan.Id, item => item with { Meter = new MeterConfig { Metric = Metrics.CpuFanDuty.Id }, Threshold = new Threshold(85, null) });
        var itself = Done(ItemEdits.AddSensor(new ThermiConfig(), 0, desktop, "/lpc/it8689e/0/fan/0"));
        itself = Done(ItemEdits.MeterItself(itself, "/lpc/it8689e/0/fan/0", desktop));

        var changed = Done(ItemEdits.ChangeSensor(byDuty, Metrics.CpuFan.Id, desktop, FanTwo));
        var changedAgain = Done(ItemEdits.ChangeSensor(changed, Metrics.CpuFan.Id, desktop, "/lpc/it8689e/0/fan/0"));

        Assert.Equal(new MeterConfig { Sensor = FanTwoDuty, Auto = Metrics.CpuFanDuty.Id }, Item(changed, Metrics.CpuFan.Id).Meter);
        Assert.Equal(new OverlayItem(null, "0 RPM", Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id }, Shown(changed, desktop)[1]);
        Assert.Equal(0.4, MeterNow(changed, Metrics.CpuFan.Id, desktop));
        Assert.Equal(new MeterConfig { Sensor = "/lpc/it8689e/0/control/0", Auto = Metrics.CpuFanDuty.Id }, Item(changedAgain, Metrics.CpuFan.Id).Meter);

        // Without its duty sensor, the fan's meter shows as missing.
        var noDuty = new Snapshot([.. Desktop.Sensors.Where(sensor => sensor.Id != FanTwoDuty)]);
        var withoutDuty = Done(ItemEdits.ChangeSensor(byDuty, Metrics.CpuFan.Id, noDuty, FanTwo));
        Assert.Equal(new OverlayItem(null, "0 RPM", Level.Normal, "8,888 RPM") { Id = Metrics.CpuFan.Id }, Shown(withoutDuty, noDuty)[1]);
        Assert.Equal(new MeterConfig { Sensor = FanTwo }, Item(Done(ItemEdits.ChangeSensor(itself, "/lpc/it8689e/0/fan/0", desktop, FanTwo)), "/lpc/it8689e/0/fan/0").Meter);
    }

    [Fact]
    public void AnItemAndItsMeterGoBackToAuto()
    {
        var config = new ThermiConfig();
        var named = Done(ItemEdits.ChangeSensor(config, Metrics.CpuFan.Id, Desktop, FanTwo));

        var auto = Done(ItemEdits.BackToAuto(named, Metrics.CpuFan.Id));
        var sensorItem = Done(ItemEdits.AddSensor(config, 0, Desktop, FanTwo));

        Assert.Equal(File(config), File(auto));
        Assert.Equal(Shown(config, Desktop), Shown(auto, Desktop));
        Assert.Equal(Refusal.NoAuto, ItemEdits.BackToAuto(sensorItem, FanTwo).Refused);
    }

    [Fact]
    public void AThresholdCanGoBackToItsDefault()
    {
        const string Duty = "/lpc/it8689e/0/control/0";
        var config = Done(ItemEdits.AddSensor(new ThermiConfig(), 0, Desktop, Duty));
        config = Done(ItemEdits.MeterItself(config, Duty, Desktop));
        string[] ids = [Metrics.CpuTemperature.Id, Metrics.CpuLoad.Id, Duty, Metrics.CpuFan.Id];
        foreach (var id in ids)
        {
            config = Done(ItemEdits.SetThreshold(config, id, 40, 95));
        }

        foreach (var id in ids)
        {
            config = Done(ItemEdits.ResetThreshold(config, id, Desktop));
        }

        Assert.Equal([new Threshold(90, 95), null, new Threshold(85, null), null], ids.Select(id => Item(config, id).Threshold));

        // A fan's speed takes lines in RPM, any other meter's from 0 to 100; both lines off leave the meter none.
        Assert.Equal(new Threshold(1500, 3000), Item(Done(ItemEdits.SetThreshold(config, Metrics.CpuFan.Id, 1500, 3000)), Metrics.CpuFan.Id).Threshold);
        Assert.Equal(Refusal.ThresholdOutOfRange, ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 50, 1500).Refused);
        Assert.Null(Item(Done(ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, null, null)), Metrics.CpuTemperature.Id).Threshold);
        Assert.Equal(Refusal.NoMeter, ItemEdits.SetThreshold(config, Metrics.CpuPower.Id, 50, 60).Refused);
    }

    [Fact]
    public void OnlyTemperaturesLoadsDutiesAndFanSpeedsCanBeMeters()
    {
        string[] sensors = ["/gpu-nvidia/0/temperature/0", "/gpu-nvidia/0/load/0", "/lpc/it8689e/0/control/0", "/gpu-nvidia/0/clock/0", "/gpu-nvidia/0/power/0", "/lpc/it8689e/0/fan/0", "/ram/data/0"];
        var dutyMeter = new MeterConfig { Sensor = "/lpc/it8689e/0/control/0" };

        var config = sensors.Aggregate(new ThermiConfig { Overlays = [new OverlayConfig { Items = [] }] }, (current, id) => Done(ItemEdits.AddSensor(current, 0, Desktop, id)));
        var itself = sensors.Select(id => ItemEdits.MeterItself(config, id, Desktop)).ToList();
        var byDuty = sensors.Aggregate(config, (current, id) => Done(ItemEdits.SetMeter(current, id, Desktop, dutyMeter)));
        var off = sensors.Aggregate(byDuty, (current, id) => Done(ItemEdits.SetMeter(current, id, Desktop, null)));

        Assert.Equal([null, null, null, Refusal.CannotBeMeter, Refusal.CannotBeMeter, null, Refusal.CannotBeMeter], itself.Select(result => result.Refused));
        Assert.Equal(Refusal.CannotBeMeter, ItemEdits.SetMeter(config, sensors[0], Desktop, new MeterConfig { Metric = Metrics.RamUsed.Id }).Refused);
        Assert.Equal(Refusal.CannotBeMeter, ItemEdits.SetMeter(config, sensors[0], Desktop, new MeterConfig { Sensor = "/gpu-nvidia/0/power/0" }).Refused);
        Assert.All(sensors, id => Assert.Equal(0.64, MeterNow(byDuty, id, Desktop)));
        Assert.All(sensors, id => Assert.Null(MeterNow(off, id, Desktop)));
        Assert.All(Shown(byDuty, Desktop), item => Assert.Equal(Level.Normal, item.Level));
        Assert.Equal([0.4193, 0.03, 0.64], itself.Take(3).Select((result, index) => MeterNow(result.Config, sensors[index], Desktop)));
        var critical = Done(ItemEdits.SetThreshold(itself[0].Config, sensors[0], 30, 40));
        Assert.Equal(Level.Critical, Shown(critical, Desktop)[0].Level);
        var unmetered = WithItem(off, sensors[0], item => item with { Threshold = new Threshold(10, 20) });
        Assert.Equal(Level.Normal, Shown(unmetered, Desktop)[0].Level);
    }

    // In each layout, and when a sensor item without a label shows the one before it, which stays its own when that
    // one is renamed.
    [Fact]
    public void RenamingAPillRenamesEveryItemInItAndNoOther()
    {
        var config = new ThermiConfig();
        var split = WithItem(config, Metrics.CpuPower.Id, item => item with { Own = true });
        var each = Laid(config, PillLayout.Each);
        var follower = new ItemConfig { Id = "ccd", Sensor = "/amdcpu/0/temperature/3" };

        var renamed = Done(ItemEdits.RenamePill(config, Metrics.CpuLoad.Id, " CHIP "));
        var renamedSplit = Done(ItemEdits.RenamePill(split, Metrics.CpuTemperature.Id, "CHIP"));
        var renamedEach = Done(ItemEdits.RenamePill(each, Metrics.CpuLoad.Id, "CHIP"));
        var renamedOne = Done(ItemEdits.RenamePill(Laid(split, PillLayout.One), Metrics.CpuTemperature.Id, "CHIP"));
        var renamedFollowed = Done(ItemEdits.RenamePill(WithOverlay(each, 0, overlay => overlay with { Items = [overlay.Items[0], follower] }), Metrics.CpuTemperature.Id, "CHIP"));

        Assert.Equal(["CHIP", "GPU", "RAM"], OverlayLayout.Groups(Shown(renamed, Desktop)).Select(group => group[0].Label));
        Assert.Equal(["CHIP", "CHIP", "CHIP", "CHIP", null, null], renamed.Overlays[0].Items.Select(item => item.Label));
        Assert.Equal(["CHIP", "CPU", "GPU", "RAM"], OverlayLayout.Groups(Shown(renamedSplit, Desktop)).Select(group => group[0].Label));
        Assert.Equal(["CHIP", "CHIP", "CHIP", null, null, null], renamedSplit.Overlays[0].Items.Select(item => item.Label));
        Assert.Equal(Refusal.LabelEmpty, ItemEdits.RenamePill(config, Metrics.CpuLoad.Id, "  ").Refused);
        Assert.Equal([Refusal.NameEmpty, Refusal.NotFound], new[] { Metrics.CpuLoad.Id, "none" }.Select(id => ItemEdits.Rename(config, id, "  ").Refused));
        Assert.Equal(["CPU", "CPU", "CHIP", "CPU", "GPU", "RAM"], Shown(renamedEach, Desktop).Select(item => item.Label));
        Assert.Equal(["CHIP", "CHIP", "CHIP", "CHIP", null, null], renamedOne.Overlays[0].Items.Select(item => item.Label));
        Assert.Equal(["CHIP", "CPU"], Shown(renamedFollowed, Desktop).Select(item => item.Label));
    }

    // R77, R29: an item can show as a tray icon of its own, and its pill is what a rename of its label changes.
    [Fact]
    public void AnItemShowsAsATrayIconAndKnowsItsPill()
    {
        var config = Done(ItemEdits.SetTray(new ThermiConfig(), Metrics.CpuLoad.Id, tray: true));
        var split = WithItem(config, Metrics.CpuPower.Id, item => item with { Own = true });

        Assert.True(config.Overlays[0].Items[2].Tray);
        Assert.False(Done(ItemEdits.SetTray(config, Metrics.CpuLoad.Id, tray: false)).Overlays[0].Items[2].Tray);
        Assert.Equal((0, 3), ItemEdits.PillOf(config.Overlays[0], 2));
        Assert.Equal((0, 2), ItemEdits.PillOf(split.Overlays[0], 2));
        Assert.Equal((2, 2), ItemEdits.PillOf(Laid(config, PillLayout.Each).Overlays[0], 2));
        Assert.Equal(Refusal.NotFound, ItemEdits.SetTray(config, "gone", tray: true).Refused);
    }

    private static ThermiConfig Laid(ThermiConfig config, PillLayout layout) =>
        WithOverlay(config, 0, overlay => overlay with { Style = overlay.Style with { Layout = layout } });

    // The warning is never above the critical, and the size stays between 75 and 200 %: each limit itself is allowed.
    [Fact]
    public void TheWindowCannotSaveAnInvalidConfig()
    {
        var config = new ThermiConfig();
        (EditResult Result, ThermiConfig Given, Refusal Refusal)[] refused =
        [
            .. RefusedEdits(config),
            (ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 90, 85), config, Refusal.WarningAboveCritical),
            (OverlayEdits.SetSize(config, 0, 0.5), config, Refusal.SizeOutOfRange),
            (OverlayEdits.SetSize(config, 0, 3), config, Refusal.SizeOutOfRange),
        ];

        Assert.All(refused, entry => Assert.Equal(((Refusal?)entry.Refusal, true), (entry.Result.Refused, ReferenceEquals(entry.Given, entry.Result.Config))));
        Assert.Contains("overlays[0].style.gap is 17", refused.Single(entry => entry.Refusal == Refusal.Invalid).Result.Problem, StringComparison.Ordinal);
        Assert.All(refused.Where(entry => entry.Refusal != Refusal.Invalid), entry => Assert.Null(entry.Result.Problem));
        Assert.Equal(new Threshold(85, 85), Item(Done(ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 85, 85)), Metrics.CpuTemperature.Id).Threshold);
        Assert.Equal(1.5, Done(OverlayEdits.SetSize(config, 0, 1.5)).Overlays[0].Size);
    }

    private static (EditResult Result, ThermiConfig Given, Refusal Refusal)[] RefusedEdits(ThermiConfig config)
    {
        var full = new ThermiConfig { Overlays = [new OverlayConfig(), new OverlayConfig(), new OverlayConfig(), new OverlayConfig()] };
        var combined = WithItem(config, Metrics.CpuTemperature.Id, item => item with { Metric = null, Sensors = ["/amdcpu/0/temperature/3", "/amdcpu/0/temperature/4"], Combine = Combine.Max });
        var power = Metrics.CpuPower.Id;
        var meterGone = WithItem(config, power, item => item with { Meter = new MeterConfig { Sensor = "/amdcpu/9/load/9" } });

        return
        [
            (ItemEdits.AddSensor(config, 1, Desktop, "/amdcpu/0/temperature/3"), config, Refusal.NotFound),
            (ItemEdits.AddSensor(config, 0, Desktop, "/amdcpu/9/temperature/9"), config, Refusal.SensorNotFound),
            (ItemEdits.Delete(config, "gone"), config, Refusal.NotFound),
            (ItemEdits.Move(config, "gone", 0), config, Refusal.NotFound),
            (ItemEdits.ChangeSensor(config, "gone", Desktop, FanTwo), config, Refusal.NotFound),
            (ItemEdits.ChangeSensor(config, Metrics.CpuFan.Id, Desktop, "/lpc/it8689e/9/fan/9"), config, Refusal.SensorNotFound),
            (ItemEdits.BackToAuto(config, "gone"), config, Refusal.NotFound),
            (ItemEdits.SetMeter(config, "gone", Desktop, null), config, Refusal.NotFound),
            (ItemEdits.SetMeter(config, power, Desktop, new MeterConfig { Sensor = "/amdcpu/9/load/9" }), config, Refusal.SensorNotFound),
            (ItemEdits.MeterItself(config, "gone", Desktop), config, Refusal.NotFound),
            (ItemEdits.MeterItself(combined, Metrics.CpuTemperature.Id, Desktop), combined, Refusal.CannotMeterItself),
            (ItemEdits.SetThreshold(config, "gone", 60, 80), config, Refusal.NotFound),
            (ItemEdits.SetThreshold(config, power, 60, 80), config, Refusal.NoMeter),
            (ItemEdits.SetThreshold(config, Metrics.CpuLoad.Id, -1, 80), config, Refusal.ThresholdOutOfRange),
            (ItemEdits.SetThreshold(config, Metrics.CpuLoad.Id, 60, double.NaN), config, Refusal.ThresholdOutOfRange),
            (ItemEdits.ResetThreshold(config, "gone", Desktop), config, Refusal.NotFound),
            (ItemEdits.ResetThreshold(config, power, Desktop), config, Refusal.NoMeter),
            (ItemEdits.ResetThreshold(meterGone, power, Desktop), meterGone, Refusal.SensorNotFound),
            (ItemEdits.RenamePill(config, "gone", "X"), config, Refusal.NotFound),
            (OverlayEdits.SetSize(config, 1, 1), config, Refusal.NotFound),
            (OverlayEdits.SetSize(config, 0, double.NaN), config, Refusal.SizeOutOfRange),
            (OverlayEdits.Snap(config, -1, new SnapConfig(SnapSpot.TopLeft)), config, Refusal.NotFound),
            (OverlayEdits.Add(full), full, Refusal.TooManyOverlays),
            (OverlayEdits.Duplicate(full, 0), full, Refusal.TooManyOverlays),
            (OverlayEdits.Duplicate(config, 1), config, Refusal.NotFound),
            (OverlayEdits.Delete(config, 0), config, Refusal.LastOverlay),
            (OverlayEdits.Delete(config, 1), config, Refusal.NotFound),
            (ConfigEdits.Apply(config, current => WithOverlay(current, 0, overlay => overlay with { Style = overlay.Style with { Gap = 17 } })), config, Refusal.Invalid),
        ];
    }

    private static IReadOnlyList<OverlayItem> Shown(ThermiConfig config, Snapshot snapshot) =>
        OverlayLayout.Build(config.Overlays[0], SensorMap.Read(snapshot, config.Overlays[0]), English);

    // What an item's meter reads now, 0 to 1, as its graph shows it, the only meter a pill draws; null without a meter.
    // Sensors report single-precision values, so it is rounded.
    private static double? MeterNow(ThermiConfig config, string id, Snapshot snapshot) =>
        Shown(WithItem(config, id, item => item with { Graph = true }), snapshot).Single(item => item.Id == id).Graph?[^1]?.Fill is { } fill ? Math.Round(fill, 4) : null;
}
