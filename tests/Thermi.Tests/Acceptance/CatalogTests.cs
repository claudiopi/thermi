using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The catalog lists every sensor the service reads, and says why one has no usable value.
/// </summary>
public sealed class CatalogTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static Snapshot NoAdmin => TestRecordings.Read(TestRecordings.NoAdmin);

    [Fact]
    public void TheCatalogGroupsEverySensorByDevice()
    {
        var devices = SensorCatalog.Build(Desktop);

        var rows = devices.SelectMany(device => device.Sensors).ToList();
        // Every sensor but a memory module's constants: its limits, resolution and size.
        var sensors = Desktop.Sensors
            .Where(sensor => !sensor.Id.StartsWith("/memory/dimm/", StringComparison.Ordinal) || sensor.Name.StartsWith("DIMM", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(sensors.Count, rows.Count);
        Assert.Equivalent(sensors, rows.Select(row => row.Sensor), strict: true);
        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(devices, device => Assert.All(device.Sensors, row => Assert.Equal(device.Name, row.Sensor.Hardware)));
        Assert.Equal(
            ["/lpc/it8689e/0", "/amdcpu/0", "/vram", "/ram", "/memory/dimm/1", "/memory/dimm/3", "/gpu-amd/0", "/gpu-nvidia/0"],
            devices.Select(device => device.Id));
        Assert.All(
            devices,
            device => Assert.Equal(
                device.Sensors.OrderBy(row => row.Reading.Kind).Select(row => row.Id),
                device.Sensors.Select(row => row.Id)));
    }

    [Fact]
    public void ASensorTheLibraryRepeatsAnIdForCanStillBeAnItem()
    {
        var rows = SensorCatalog.Build(Desktop).SelectMany(device => device.Sensors).Where(row => row.Sensor.Id == "/gpu-nvidia/0/load/3").ToList();

        Assert.Equal([("/gpu-nvidia/0/load/3", "GPU Bus"), ("/gpu-nvidia/0/load/3#2", "GPU Memory")], rows.Select(row => (row.Id, row.Sensor.Name)));
        var config = Edited.Done(ItemEdits.AddSensor(new ThermiConfig(), 0, Desktop, rows[1].Id));
        var shown = OverlayLayout.Build(config.Overlays[0], SensorMap.Read(Desktop, config.Overlays[0]), English);
        Assert.Equal(("GPU", "20 %"), (Edited.Item(config, rows[1].Id).Label, shown.Single(item => item.Id == rows[1].Id).Value));
        Assert.Equal("36 %", ReadingFormatter.Format(SensorMap.ReadingOf(SensorMap.Find(Desktop, rows[0].Id)!)!.Value, English));
        Assert.Null(SensorMap.Find(Desktop, "/gpu-nvidia/0/load/3#3"));
        Assert.Null(SensorMap.Find(Desktop, "/gpu-nvidia/0/load/3#1"));
    }

    [Fact]
    public void TheCatalogFindsSensorsByNameDeviceOrType()
    {
        var devices = SensorCatalog.Build(Desktop);

        var fans = SensorCatalog.Filter(devices, "fan", []);
        var temperatures = SensorCatalog.Filter(devices, "", [SensorKind.Temperature]);
        var nvidiaLoads = SensorCatalog.Filter(devices, " NVIDIA  load ", [SensorKind.Load]);

        Assert.Equal(["/lpc/it8689e/0", "/gpu-nvidia/0"], fans.Select(device => device.Id));
        Assert.Equal(
            ["Fan #1", "Fan #2", "Fan #3", "Fan #4", "Fan #5", "Fan #1", "Fan #2", "Fan #3", "Fan #4", "Fan #5", "Fan #6"],
            fans[0].Sensors.Select(row => row.Sensor.Name));
        Assert.Equal([("GPU Fan", SensorKind.Fan), ("GPU Fan", SensorKind.Control)], fans[1].Sensors.Select(row => (row.Sensor.Name, row.Reading.Kind)));
        Assert.Equal(
            Desktop.Sensors.Count(sensor => sensor.Type == "Temperature" && !SensorCatalog.Constant(sensor)),
            temperatures.Sum(device => device.Sensors.Count));
        Assert.All(temperatures.SelectMany(device => device.Sensors), row => Assert.Equal(SensorKind.Temperature, row.Reading.Kind));
        Assert.Equal(["/gpu-nvidia/0"], nvidiaLoads.Select(device => device.Id));
        Assert.Equal(Desktop.Sensors.Count(sensor => sensor.HardwareType == "GpuNvidia" && sensor.Type == "Load"), nvidiaLoads[0].Sensors.Count);
        Assert.Empty(SensorCatalog.Filter(devices, "fan", [SensorKind.Temperature]));

        var spanish = SensorCatalog.Filter(devices, "ventilador", [], Thermi.Core.Translations.Texts.Of("es")).SelectMany(device => device.Sensors).ToList();
        Assert.NotEmpty(spanish);
        Assert.Equal(SensorCatalog.Filter(devices, "fan", [SensorKind.Fan]).Sum(device => device.Sensors.Count), spanish.Count);
        Assert.All(spanish, row => Assert.Equal(SensorKind.Fan, row.Reading.Kind));
        Assert.Empty(SensorCatalog.Filter(devices, "ventilador", []));

        // Each synonym finds its type, and a word that finds nothing offers the one word nearest to it that does.
        foreach (var (synonym, kind) in new[]
        {
            ("heat", SensorKind.Temperature),
            ("usage", SensorKind.Load),
            ("rpm", SensorKind.Fan),
            ("watts", SensorKind.Power),
        })
        {
            Assert.Equal(
                SensorCatalog.Filter(devices, "", [kind]).Sum(device => device.Sensors.Count),
                SensorCatalog.Filter(devices, synonym, []).Sum(device => device.Sensors.Count));
        }

        var suggested = SensorCatalog.Suggest(devices, "tempreature");
        Assert.NotNull(suggested);
        Assert.NotEmpty(SensorCatalog.Filter(devices, suggested, []));
        Assert.Null(SensorCatalog.Suggest(devices, "fan"));
        // A word far from every word that finds something offers none, since a far one would only puzzle.
        Assert.Null(SensorCatalog.Suggest(devices, "qqqqqq"));

        // A word matches at the start of a word, so "ip" finds the address and not a chipset; a sensor's own name finds it
        // alone, not every sensor of its device.
        var windows = SensorCatalog.Build(SystemSensors.With(Desktop, new WindowsFacts(TimeSpan.FromHours(2), 3232235799, DateTimeOffset.UnixEpoch)));
        foreach (var (word, sensor) in new[] { ("uptime", SystemSensors.Uptime), ("ip", SystemSensors.LocalIp) })
        {
            Assert.Equal([sensor], SensorCatalog.Filter(windows, word, []).SelectMany(device => device.Sensors).Select(row => row.Sensor.Name));
        }
    }

    [Fact]
    public void WithoutTheDriverTheCatalogSaysWhyTheCpuReadsNothing()
    {
        var cpu = SensorCatalog.Build(NoAdmin).Single(device => device.Id == "/amdcpu/0").Sensors;

        var zeros = cpu.Where(row => row.Sensor.Value is 0 && row.Reading.Kind is SensorKind.Temperature or SensorKind.Power or SensorKind.Clock).ToList();
        var empty = cpu.Where(row => row.Sensor.Value is null).ToList();

        Assert.Equal(36, zeros.Count);
        Assert.All(zeros, row => Assert.Equal((NoValue?)NoValue.DriverMayBeMissing, row.Why));
        Assert.All(zeros, row => Assert.Equal(ReadingFormatter.Missing, ReadingFormatter.Format(row.Reading, English)));
        Assert.Equal(16, empty.Count);
        Assert.All(empty, row => Assert.Equal((SensorKind.Clock, (NoValue?)NoValue.ReadsNothing), (row.Reading.Kind, row.Why)));
        Assert.All(cpu.Where(row => row.Reading.Kind == SensorKind.Load), row => Assert.Null(row.Why));
        Assert.Contains(cpu, row => row is { Reading: { Kind: SensorKind.Load, Value: 0 }, Why: null });
    }

    [Fact]
    public void TheCatalogSaysWhyAFanReadsZeroAndKeepsOtherZeros()
    {
        const string Idle = "/amdcpu/0/load/20";
        var rows = SensorCatalog.Build(Desktop).SelectMany(device => device.Sensors).ToList();
        var nan = new Snapshot([.. Desktop.Sensors.Select(sensor => sensor.Id == "/lpc/it8689e/0/fan/0" ? sensor with { Value = double.NaN } : sensor)]);

        var config = Edited.Done(ItemEdits.AddSensor(new ThermiConfig(), 0, Desktop, Idle));
        var item = OverlayLayout.Build(config.Overlays[0], SensorMap.Read(Desktop, config.Overlays[0]), English).Single(each => each.Id == Idle);

        Assert.Equal(
            ["/lpc/it8689e/0/fan/1", "/lpc/it8689e/0/fan/2", "/lpc/it8689e/0/fan/3", "/lpc/it8689e/0/fan/4"],
            rows.Where(row => row.Why == NoValue.Stopped).Select(row => row.Id));
        // A GPU stops its fans while it is cool, so its 0 RPM is a fan at rest, never one not connected.
        Assert.Equal(["/gpu-nvidia/0/fan/1"], rows.Where(row => row.Why == NoValue.Resting).Select(row => row.Id));
        Assert.All(
            rows.Where(row => row.Why is NoValue.Stopped or NoValue.Resting),
            row => Assert.Equal("0 RPM", ReadingFormatter.Format(row.Reading, English)));
        var idle = rows.Single(row => row.Id == Idle);
        Assert.Equal(("0 %", null), (ReadingFormatter.Format(idle.Reading, English), idle.Why));
        Assert.Equal("0 %", item.Value);
        Assert.Equal(
            [
                "/lpc/it8689e/0/control/1",
                "/lpc/it8689e/0/control/2",
                "/lpc/it8689e/0/control/3",
                "/lpc/it8689e/0/control/4",
                "/lpc/it8689e/0/control/5",
            ],
            rows.Where(row => row.Why == NoValue.ReadsNothing).Select(row => row.Id));
        Assert.Equal(NoValue.ReadsNothing, SensorCatalog.Build(nan)[0].Sensors.Single(row => row.Id == "/lpc/it8689e/0/fan/0").Why);
    }

    [Fact]
    public void TheOverlayTreatsZeroAsMissingOnlyForTheCpusTemperaturePowerAndClock()
    {
        var idle = new Snapshot([.. Desktop.Sensors.Select(sensor => sensor.Value is null ? sensor : sensor with { Value = 0 })]);
        var read = Metrics.All.Where(metric => SensorMap.Chosen(idle, new OverlayConfig())[metric.Id] is not null).ToList();

        var readings = SensorMap.Read(idle, new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal(
            [Metrics.CpuTemperature.Id, Metrics.CpuPower.Id, Metrics.CpuClock.Id],
            read.Where(metric => readings[metric.Id].Value is null).Select(metric => metric.Id));
        Assert.All(read.Where(metric => readings[metric.Id].Value is not null), metric => Assert.Equal(0.0, readings[metric.Id].Value));
    }

    // The window names the sensor beside "Auto": the one the metric picks, or the one an item of the overlay names for it.
    [Fact]
    public void AutoNamesTheSensorItsMetricReads()
    {
        var overlay = new OverlayConfig { Items = [new ItemConfig { Id = "gpu", Sensor = "/gpu-nvidia/0/temperature/3", Auto = Metrics.CpuTemperature.Id }] };

        var picked = SensorMap.Chosen(Desktop, new OverlayConfig());
        var named = SensorMap.Chosen(Desktop, overlay);

        Assert.Equal(("Core (Tctl/Tdie)", "AMD Ryzen 9 9950X"), (picked[Metrics.CpuTemperature.Id]!.Name, picked[Metrics.CpuTemperature.Id]!.Hardware));
        Assert.Equal("/lpc/it8689e/0/control/0", picked[Metrics.CpuFanDuty.Id]?.Id);
        Assert.All(SensorMap.Chosen(new Snapshot([]), overlay).Values, Assert.Null);
        Assert.Equal("GPU Memory Junction", named[Metrics.CpuTemperature.Id]?.Name);
        Assert.Equal(Metrics.All.Select(metric => metric.Id), named.Keys);
        NetworkDiskAndBatterySitInTheirGroups();
        AutoFollowsTheAdapterThatMovedMostAndTheDriveBusiestOverAMinute();
    }

    // The recorded adapters under Network, virtual ones included, and drives under Disk, each a device of its own, found by
    // their words and the Throughput chip; a battery under Battery, a group the desktops do not have.
    private static void NetworkDiskAndBatterySitInTheirGroups()
    {
        var desktop = SensorCatalog.Build(MadeUpHardware.Desktop());
        var devices = MadeUpHardware.Desktop().Sensors
            .Where(sensor => sensor.HardwareType is "Network" or "Storage")
            .Select(sensor => (Id: string.Join('/', sensor.Id.Split('/')[..3]), Kind: sensor.HardwareType == "Network" ? DeviceKind.Network : DeviceKind.Ssd))
            .Distinct()
            .ToList();
        string[] adapters = [.. devices.Where(device => device.Kind == DeviceKind.Network).Select(device => device.Id)];
        string[] drives = [MadeUpHardware.Samsung, MadeUpHardware.Western];

        Assert.Equal(13, devices.Count);
        Assert.Equal(devices, desktop.Where(device => device.Kind is DeviceKind.Network or DeviceKind.Ssd).Select(device => (device.Id, device.Kind!.Value)));
        foreach (var (word, finds) in new[]
        {
            ("net", adapters), ("network", adapters), ("internet", adapters), ("ethernet", adapters), ("wifi", adapters), ("disk", drives),
        })
        {
            var found = SensorCatalog.Filter(desktop, word, []).Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
            Assert.Superset(finds.ToHashSet(StringComparer.Ordinal), found);
            Assert.Subset(adapters.Concat(drives).ToHashSet(StringComparer.Ordinal), found);
        }

        // The GPU's PCIe link is a throughput too, two of them beside each adapter's and each drive's two.
        Assert.Equal(
            2 * (adapters.Length + drives.Length + 1),
            SensorCatalog.Filter(desktop, "", [SensorKind.Throughput]).Sum(device => device.Sensors.Count));
        Assert.Equal(
            [MadeUpHardware.Battery],
            SensorCatalog.Build(MadeUpHardware.Laptop()).Where(device => device.Kind == DeviceKind.Battery).Select(device => device.Id));
        Assert.DoesNotContain(SensorCatalog.Build(Desktop).Concat(SensorCatalog.Build(NoAdmin)), device => device.Kind == DeviceKind.Battery);
    }

    // The adapter that moved the most data, a physical one while any moves data, a count it could not read counting as none;
    // the drive busiest over a minute, chosen again at most once a minute, or busiest now without that history, an activity
    // it could not read counting as none; an item naming its own keeps it.
    private static void AutoFollowsTheAdapterThatMovedMostAndTheDriveBusiestOverAMinute()
    {
        var desktop = MadeUpHardware.Desktop();
        Metric[] followers = [Metrics.NetDownload, Metrics.NetUpload, Metrics.DiskActivity, Metrics.DiskRead, Metrics.DiskWrite];
        var wifi = new OverlayConfig
        {
            Items = [new ItemConfig { Id = "wifi", Sensor = $"{MadeUpHardware.WiFi}/throughput/8", Auto = Metrics.NetDownload.Id }],
        };
        var time = new FakeTimeProvider();
        var drive = new BusiestDrive(time);

        // Two seconds of the first minute read the busy drive's activity as NaN and as null, which must not cost it the minute.
        var followed = Enumerable.Range(0, 150).Select(second =>
        {
            var busy = Busy(desktop, second < 60 ? MadeUpHardware.Samsung : MadeUpHardware.Western);
            busy = second switch
            {
                30 => Unread(busy, $"{MadeUpHardware.Samsung}/load/53", sensor => sensor with { Value = double.NaN }),
                31 => Unread(busy, $"{MadeUpHardware.Samsung}/load/53", sensor => sensor with { Value = null }),
                _ => busy,
            };
            var chosen = drive.Add(busy);
            time.Advance(TimeSpan.FromSeconds(1));
            return chosen;
        }).ToList();

        Assert.Equal(
            [
                $"{MadeUpHardware.Ethernet}/throughput/8",
                $"{MadeUpHardware.Ethernet}/throughput/7",
                $"{MadeUpHardware.Samsung}/load/53",
                $"{MadeUpHardware.Samsung}/throughput/54",
                $"{MadeUpHardware.Samsung}/throughput/55",
            ],
            followers.Select(metric => SensorMap.Chosen(desktop, new OverlayConfig())[metric.Id]?.Id));
        Assert.Equal($"{MadeUpHardware.WiFi}/throughput/8", SensorMap.Chosen(desktop, wifi)[Metrics.NetDownload.Id]?.Id);
        Assert.Equal($"{MadeUpHardware.Western}/load/53", SensorMap.Chosen(desktop, new OverlayConfig(), MadeUpHardware.Western)[Metrics.DiskActivity.Id]?.Id);
        Assert.Equal([.. Enumerable.Repeat(MadeUpHardware.Samsung, 120), .. Enumerable.Repeat(MadeUpHardware.Western, 30)], followed);
        Assert.Equal($"{MadeUpHardware.WiFi}/throughput/8", SensorMap.Chosen(MadeUpHardware.Laptop(), new OverlayConfig())[Metrics.NetDownload.Id]?.Id);
        Assert.Equal(
            $"{MadeUpHardware.Wsl}/throughput/8",
            SensorMap.Chosen(MadeUpHardware.Laptop(wifiMoved: 0), new OverlayConfig())[Metrics.NetDownload.Id]?.Id);
        foreach (var unread in new Func<RawSensor, RawSensor?>[]
        {
            sensor => sensor with { Value = double.NaN },
            sensor => sensor with { Value = null },
            _ => null,
        })
        {
            // Ethernet's upload count unread still leaves its download count, the most; the recorded drive's activity
            // unread leaves the other drive, barely busy, the busiest.
            var broken = Unread(Unread(desktop, $"{MadeUpHardware.Ethernet}/data/2", unread), $"{MadeUpHardware.Samsung}/load/53", unread);
            Assert.Equal(
                [$"{MadeUpHardware.Ethernet}/throughput/8", $"{MadeUpHardware.Western}/load/53"],
                new[] { Metrics.NetDownload, Metrics.DiskActivity }.Select(metric => SensorMap.Chosen(broken, new OverlayConfig())[metric.Id]?.Id));
        }
    }

    private static Snapshot Busy(Snapshot snapshot, string drive) => snapshot with
    {
        Sensors =
        [
            .. snapshot.Sensors.Select(sensor => sensor.Name == "Total Activity"
                ? sensor with { Value = sensor.Id.StartsWith(drive + "/", StringComparison.Ordinal) ? 90 : 2 }
                : sensor),
        ],
    };

    // The snapshot with one sensor as the library gives one it could not read: NaN, null, or not at all.
    private static Snapshot Unread(Snapshot snapshot, string id, Func<RawSensor, RawSensor?> unread) => snapshot with
    {
        Sensors = [.. snapshot.Sensors.Select(sensor => sensor.Id == id ? unread(sensor) : sensor).OfType<RawSensor>()],
    };

    [Fact]
    public void ASensorOfATypeThermiDoesNotShowHasNoRow()
    {
        var withVoltage = new Snapshot(
        [
            .. Desktop.Sensors,
            new RawSensor("/lpc/it8689e/0/voltage/0", "SuperIO", "ITE IT8689E", "Voltage", "Vcore", 1.1),
            new RawSensor("odd", "SuperIO", "Odd", "Fan", "Odd", 1),
        ]);

        var devices = SensorCatalog.Build(withVoltage);

        Assert.Equal(SensorCatalog.Build(Desktop).Sum(device => device.Sensors.Count) + 1, devices.Sum(device => device.Sensors.Count));
        Assert.Equal("Odd", devices[^1].Id);
    }
}
