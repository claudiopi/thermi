using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The catalog names what a board's chip only numbers, hides what nobody would add, and takes the names people give.
/// </summary>
public sealed class ReadableCatalogTests
{
    private const string Ite = "/lpc/it8689e/0";

    private const string Nuvoton = "/lpc/nct6798d/0/temperature/0";

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static Snapshot NoAdmin => TestRecordings.Read(TestRecordings.NoAdmin);

    // The recording's board sensor of the CPU reads what the CPU does, 76 °C, which bears out the third input of the table.
    [Fact]
    public void TheCatalogNamesBoardInputsLikelyAndSaysSo()
    {
        var withNuvoton = new Snapshot([.. Desktop.Sensors, new RawSensor(Nuvoton, "SuperIO", "Nuvoton NCT6798D", "Temperature", "Temperature #1", 41)]);
        var devices = SensorCatalog.Build(withNuvoton);
        var rows = devices.SelectMany(device => device.Sensors).ToList();
        var spanish = Texts.Of("es");

        Assert.Equal(
            [
                ("/temperature/0", Texts.English.Of(LikelyName.System), "Temperature #1"),
                ("/temperature/1", Texts.English.Of(LikelyName.Chipset), "Temperature #2"),
                ("/temperature/2", Texts.English.Of(LikelyName.Cpu), "Temperature #3"),
                ("/temperature/3", Texts.English.Of(LikelyName.PcieSlot), "Temperature #4"),
                ("/temperature/4", Texts.English.Of(LikelyName.Vrm), "Temperature #5"),
                ("/fan/0", Texts.English.Of(LikelyName.CpuFan), "Fan #1"),
                ("/fan/1", Texts.English.Of(LikelyName.SystemFan1), "Fan #2"),
                ("/fan/2", Texts.English.Of(LikelyName.SystemFan2), "Fan #3"),
                ("/control/0", Duty(LikelyName.CpuFan), "Fan #1"),
                ("/control/1", Duty(LikelyName.SystemFan1), "Fan #2"),
                ("/control/2", Duty(LikelyName.SystemFan2), "Fan #3"),
            ],
            rows.Where(row => row.Name.From == NameFrom.Likely).Select(row => (row.Id[Ite.Length..], row.Name.Shown, row.Name.Library)));
        Assert.All(
            rows.Where(row => row.Name.From == NameFrom.Likely),
            row => Assert.Equal(
                ("ITE IT8689E", int.Parse(row.Name.Library.Split('#')[1], CultureInfo.InvariantCulture)),
                (row.Name.Chip, row.Name.Input)));
        Assert.Equal(
            [
                ($"{Ite}/temperature/5", 6),
                ($"{Ite}/fan/3", 4),
                ($"{Ite}/fan/4", 5),
                ($"{Ite}/control/3", 4),
                ($"{Ite}/control/4", 5),
                ($"{Ite}/control/5", 6),
                (Nuvoton, 1),
            ],
            rows.Where(row => row.Name is { From: NameFrom.Library, Chip: not null }).Select(row => (row.Id, row.Name.Input!.Value)));
        Assert.All(rows.Where(row => row.Name.Chip is not null), row => Assert.Equal(row.Sensor.Hardware, row.Name.Chip));
        // Any other sensor keeps the library's name, but "Fan" in it reads "fan" (QA round 5, P9), a duty, which the library
        // names as its fan, says it is the duty, as "GPU fan duty", and a name another type of its device shares says its type
        // (cold review 4, Q5).
        bool Shared(CatalogSensor row)
        {
            return row.Reading.Kind != SensorKind.Control && rows.Exists(other =>
                other.Sensor.Hardware == row.Sensor.Hardware && other.Sensor.Name == row.Sensor.Name
                && other.Reading.Kind != row.Reading.Kind && other.Reading.Kind != SensorKind.Control);
        }

        Assert.All(
            rows.Where(row => !row.Id.StartsWith("/lpc/", StringComparison.Ordinal)),
            row => Assert.Equal(
                new SensorName(
                    row.Reading.Kind == SensorKind.Control ? Duty(row.Sensor.Name)
                    : Shared(row) ? WithKind(row.Sensor.Name, row.Reading.Kind)
                    : row.Reading.Kind == SensorKind.Fan ? row.Sensor.Name.Replace(" Fan", " fan", StringComparison.Ordinal)
                    : row.Sensor.Name,
                    NameFrom.Library,
                    row.Sensor.Name),
                row.Name));
        Assert.True(Shared(rows.Single(row => row.Id == "/gpu-nvidia/0/temperature/0")));
        Assert.All(
            rows.Where(row => row is { Name.From: NameFrom.Library, Reading.Kind: SensorKind.Control }),
            row => Assert.Equal(Duty(row.Sensor.Name), row.Name.Shown));

        var cpu = SensorCatalog.Build(Desktop, texts: spanish).SelectMany(device => device.Sensors).Single(row => row.Id == $"{Ite}/temperature/2");
        Assert.Equal(spanish.Of(LikelyName.Cpu), cpu.Name.Shown);
        Assert.Equal(
            [$"{Ite}/temperature/4"],
            SensorCatalog.Filter(devices, Texts.English.Of(LikelyName.Vrm).ToLowerInvariant(), [])
                .SelectMany(device => device.Sensors).Select(row => row.Id));
        // The word its note starts with finds every probable name, as the row reads it.
        var likelyWord = Texts.English.Of(CatalogText.LikelyNote).Split(' ')[0];
        Assert.Equal(
            rows.Where(row => row.Name.From == NameFrom.Likely).Select(row => row.Id),
            SensorCatalog.Filter(devices, likelyWord, []).SelectMany(device => device.Sensors).Select(row => row.Id));
    }

    [Fact]
    public void TheCatalogHidesWhatNobodyWouldAddAndCountsIt()
    {
        var devices = SensorCatalog.Build(Desktop);
        var rows = devices.SelectMany(device => device.Sensors).ToList();
        var layers = rows
            .Where(row => row.Sensor.HardwareType.StartsWith("Gpu", StringComparison.Ordinal)
                && row.Sensor.Name.StartsWith("D3D ", StringComparison.Ordinal))
            .Select(row => row.Id).ToList();

        var (shown, hidden) = SensorCatalog.Hide(devices, new HashSet<string>(StringComparer.Ordinal));
        var seen = SensorCatalog.Hide(devices, new HashSet<string>([$"{Ite}/fan/1"], StringComparer.Ordinal))
            .Shown.SelectMany(device => device.Sensors);
        var (searched, searchHidden) = SensorCatalog.Hide(SensorCatalog.Filter(devices, "d3d", []), new HashSet<string>(StringComparer.Ordinal));
        var noAdmin = SensorCatalog.Hide(SensorCatalog.Build(NoAdmin), new HashSet<string>(StringComparer.Ordinal))
            .Shown.SelectMany(device => device.Sensors).ToList();

        string[] nothing = [.. Enumerable.Range(1, 5).Select(index => $"{Ite}/control/{index}")];
        // The GPU's fan reads 0 RPM too, but a GPU stops its fans while cool, so it stays.
        string[] stopped = [.. Enumerable.Range(1, 4).Select(index => $"{Ite}/fan/{index}")];
        // The CPU's bus speed, which never moves, and its effective clocks, the time its cores ran: each core's and their average.
        string[] effective = ["/amdcpu/0/clock/0", "/amdcpu/0/clock/2", .. Enumerable.Range(0, 16).Select(core => $"/amdcpu/0/clock/{4 + (2 * core)}")];
        Assert.Equal(24, layers.Count);
        Assert.Equal(
            nothing.Concat(stopped).Concat(layers).Concat(effective).Order(StringComparer.Ordinal),
            rows.Select(row => row.Id)
                .Except(shown.SelectMany(device => device.Sensors).Select(row => row.Id), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        Assert.Equal(nothing.Length + stopped.Length + layers.Count + effective.Length, hidden);

        // Cold review 4 (Q4): the Sensor dropdown leaves out what the catalog hides whatever was seen, but a stopped fan, which
        // a CPU fan may be at idle, and a second sensor of a device with the type and name of one before it.
        var twin = rows.Single(row => row.Id == "/gpu-nvidia/0/load/0");
        var twice = devices
            .Select(device => device with
            {
                Sensors = [.. device.Sensors, .. device.Sensors.Where(row => row == twin).Select(row => row with { Id = "twin" })],
            })
            .ToList();
        Assert.Equal(
            nothing.Concat(layers).Concat(effective).Order(StringComparer.Ordinal),
            SensorCatalog.Unoffered(devices).Order(StringComparer.Ordinal));
        Assert.Equal(["twin"], SensorCatalog.Unoffered(twice).Except(SensorCatalog.Unoffered(devices)));
        Assert.DoesNotContain(shown, device => device.Sensors.Count == 0);
        Assert.Contains(seen, row => row is { Id: $"{Ite}/fan/1", Why: NoValue.Stopped });
        Assert.Contains(noAdmin, row => row is { Id: "/gpu-nvidia/0/fan/1", Why: NoValue.Resting });
        // A memory module's limits, resolution and size never move, so of a module the catalog lists its temperature alone.
        Assert.Equal(
            ["/memory/dimm/1/temperature/0", "/memory/dimm/3/temperature/0"],
            rows.Where(row => row.Id.StartsWith("/memory/dimm/", StringComparison.Ordinal)).Select(row => row.Id));

        // Of the recorded adapters only the Ethernet one moves data; the idle ones show once they have moved any.
        Assert.Equal([MadeUpHardware.Ethernet], Adapters(new HashSet<string>(StringComparer.Ordinal)));
        Assert.Equal(
            [MadeUpHardware.Ethernet, MadeUpHardware.WiFi],
            Adapters(new HashSet<string>([$"{MadeUpHardware.WiFi}/throughput/8"], StringComparer.Ordinal)));

        // A drive's fixed limits and wear counters never move, so the catalog lists only its temperatures, load and rates.
        Assert.Equal(
            [
                "/nvme/0/temperature/0",
                "/nvme/0/temperature/1",
                "/nvme/0/temperature/2",
                "/nvme/0/load/30",
                "/nvme/0/load/51",
                "/nvme/0/load/52",
                "/nvme/0/load/53",
            ],
            SensorCatalog.Build(TestRecordings.Read(TestRecordings.NetDisk)).Single(device => device.Id == "/nvme/0")
                .Sensors.Where(row => row.Reading.Kind is SensorKind.Temperature or SensorKind.Load).Select(row => row.Id));
        Assert.Equal((0, layers.Count), (searched.Count, searchHidden));
        // Without the driver the cores' clocks say how to fix it; their effective twins stay hidden.
        Assert.Equal(19, noAdmin.Count(row => row.Why == NoValue.DriverMayBeMissing));
        Assert.DoesNotContain(noAdmin, row => row.Why == NoValue.ReadsNothing);
    }

    // A fan's duty keeps its type in its name, so it never reads as the fan itself.
    private static string Duty(LikelyName name) => Texts.English.Format(CultureInfo.InvariantCulture, CatalogText.LikelyDuty, Texts.English.Of(name));

    // A duty the library names as its fan, "GPU Fan", reads "GPU fan duty", in thermi's case.
    private static string Duty(string library) =>
        Texts.English.Format(CultureInfo.InvariantCulture, CatalogText.LikelyDuty, library.Replace(" Fan", " fan", StringComparison.Ordinal));

    // A name another type of the device shares, with its own type after it.
    private static string WithKind(string library, SensorKind kind) =>
        Texts.English.Format(CultureInfo.InvariantCulture, CatalogText.WithKind, library, Texts.English.Of(kind).ToLowerInvariant());

    // The recorded adapters the catalog shows, given the sensors seen moving since it opened.
    private static string[] Adapters(IReadOnlySet<string> moved) =>
        [
            .. SensorCatalog.Hide(SensorCatalog.Build(TestRecordings.Read(TestRecordings.NetDisk)), moved)
                .Shown.Where(device => device.Kind == DeviceKind.Network).Select(device => device.Id),
        ];

    // A new item is named for what its sensor reads, added to the default overlay: as its metric, after an equals sign;
    // else by its device and the sensor's readable name, or that name alone; a duty says it is one, and a sensor a
    // default item already shows takes a number. Named by thermi alone, told apart and numbered too, it stays unnamed in
    // the file and reads in the window's language as if added in it (night QA: a GPU fan stayed "GPU fan" in Spanish, and
    // one told apart kept the Spanish name it was added with).
    [Theory]
    [InlineData("/gpu-nvidia/0/fan/1", DeviceKind.Gpu, "fan", "")]
    [InlineData("/nvme/0/load/30", DeviceKind.Ssd, "used space", "")]
    [InlineData("/nvme/0/temperature/0", DeviceKind.Ssd, "temperature", "")]
    [InlineData("/vram/data/2", null, "Virtual memory used", "")]
    [InlineData("/gpu-nvidia/0/control/1", null, "GPU fan", "duty")]
    [InlineData("/lpc/it8689e/0/control/3", null, "Fan #4", "duty")]
    [InlineData("/amdcpu/0/power/0", null, "=cpu.power", "2")]
    // Told apart by its device or sensor without their brackets or makers' marks (night QA: "CPU temperature (CCD2 (Tdie))").
    [InlineData("/gpu-amd/0/clock/0", DeviceKind.Gpu, "frequency", "apart AMD Radeon Graphics")]
    [InlineData("/amdcpu/0/temperature/4", DeviceKind.Cpu, "temperature", "apart CCD2")]
    public void ANewItemIsNamedForWhatItsSensorReads(string sensor, DeviceKind? device, string name, string form)
    {
        var texts = Texts.English;
        var read = Metrics.Find(name.TrimStart('=')) is { } metric && name[0] == '=' ? texts.Of(metric.Name) : name;
        var plain = device is { } kind ? texts.Format(CultureInfo.InvariantCulture, Enum.Parse<OfDevice>(kind.ToString()), read) : read;
        var expected = form switch
        {
            "duty" => texts.Format(CultureInfo.InvariantCulture, CatalogText.LikelyDuty, plain),
            "" => plain,
            _ when form.StartsWith("apart ", StringComparison.Ordinal) => texts.Format(CultureInfo.InvariantCulture, NameText.AfterSensor, plain, form[6..]),
            _ => texts.Format(CultureInfo.InvariantCulture, NameText.Numbered, plain, form),
        };
        var snapshot = TestRecordings.Read(TestRecordings.NetDisk);
        var spanish = Texts.Of("es");
        ThermiConfig Added(Texts language)
        {
            return Done(ItemEdits.AddSensor(new ThermiConfig(), 0, snapshot, sensor, language));
        }

        string? Shown(ThermiConfig config, Texts language)
        {
            return PageSettings.Of(config, language, snapshot).Overlays[0].Items.Single(item => item.Sensor == sensor).Name;
        }

        var added = Added(texts);

        Assert.Equal(expected, Shown(added, texts));
        Assert.Equal(
            (null, Shown(Added(spanish), spanish)),
            (Added(spanish).Overlays[0].Items.Single(item => item.Sensor == sensor).Name, Shown(added, spanish)));

        // Spanish names a device as its metrics do: "Ventilador de la GPU", as "Carga de la GPU" (night QA: "Ventilador de GPU").
        if (device is DeviceKind.Cpu or DeviceKind.Gpu)
        {
            var load = spanish.Of(device == DeviceKind.Cpu ? MetricName.CpuLoad : MetricName.GpuLoad);
            Assert.Contains(load[load.IndexOf(' ', StringComparison.Ordinal)..], Shown(added, spanish), StringComparison.Ordinal);
        }
    }

    // QA round 5 (R5-08): a sensor of the CPU's graphics chip, beside a graphics card the GPU pill reads, starts an iGPU
    // pill, and a sensor of the card added after it still joins the GPU pill, as one of the chip joins the iGPU one.
    [Fact]
    public void ASecondGpusSensorsGoToAPillOfTheirOwn()
    {
        var added = new[] { "/gpu-amd/0/clock/0", "/gpu-nvidia/0/control/1", "/gpu-amd/0/load/0" }
            .Aggregate(new ThermiConfig(), (config, sensor) => Done(ItemEdits.AddSensor(config, 0, Desktop, sensor)));

        Assert.Equal(
            ["CPU", "CPU", "CPU", "CPU", "GPU", "GPU", "RAM", "iGPU", "iGPU"],
            Thermi.Core.Look.OverlayLayout.RunLabels(added.Overlays[0].Items));
        Assert.Equal(
            ["gpu.load", "/gpu-nvidia/0/control/1", "ram.used", "/gpu-amd/0/clock/0", "/gpu-amd/0/load/0"],
            added.Overlays[0].Items.Skip(4).Select(item => item.Sensor ?? item.Metric));
    }

    // Two items of one board sensor, the second renamed by hand, beside the CPU fan item, whose metric picks the board's first fan.
    [Fact]
    public void ASensorRenamedInTheCatalogIsNamedSoEverywhere()
    {
        const string Chipset = $"{Ite}/temperature/1";
        var history = new UndoHistory();
        var twice = Done(ItemEdits.AddSensor(Done(ItemEdits.AddSensor(new ThermiConfig(), 0, Desktop, Chipset)), 0, Desktop, Chipset));
        var ids = twice.Overlays[0].Items.Where(item => item.Sensor == Chipset).Select(item => item.Id).ToList();
        var start = Done(ItemEdits.Rename(twice, ids[1], "Desk"));

        var renamed = Done(history, start, config => ThermiEdits.RenameSensor(config, Desktop, Chipset, "  Fan hub  "));
        var fan = Done(history, renamed, config => ThermiEdits.RenameSensor(config, Desktop, $"{Ite}/fan/0", "Front intake"));
        var row = SensorCatalog.Build(Desktop, renamed.SensorNames).SelectMany(device => device.Sensors).Single(each => each.Id == Chipset);
        var withAdded = Done(ItemEdits.AddSensor(renamed, 0, Desktop, Chipset));
        var added = withAdded.Overlays[0].Items.Single(item => item.Sensor == Chipset && !ids.Contains(item.Id));
        var cleared = Done(ThermiEdits.RenameSensor(renamed, Desktop, Chipset, " "));

        Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { [Chipset] = "Fan hub" }, renamed.SensorNames);
        Assert.Equal(("Fan hub", "Desk"), (Item(renamed, ids[0]).Name, Item(renamed, ids[1]).Name));
        Assert.Equal(new SensorName("Fan hub", NameFrom.Own, "Temperature #2", "ITE IT8689E", 2), row.Name);
        Assert.Equal(
            [Chipset],
            SensorCatalog.Filter(SensorCatalog.Build(Desktop, renamed.SensorNames), "hub", [])
                .SelectMany(device => device.Sensors).Select(each => each.Id));
        Assert.Null(Item(fan, Metrics.CpuFan.Id).Name);
        Assert.Equal(
            Texts.English.Format(CultureInfo.InvariantCulture, NameText.Numbered, "Fan hub", 2),
            Names.Of(withAdded, added, Desktop, Texts.English));
        Assert.Equal((null, Item(start, ids[0]).Name), (cleared.SensorNames, Item(cleared, ids[0]).Name));
        Assert.Equal(Refusal.SensorNameTooLong, ThermiEdits.RenameSensor(renamed, Desktop, Chipset, new string('x', SensorNames.MaxLength + 1)).Refused);
        Assert.Equal(Refusal.SensorNotFound, ThermiEdits.RenameSensor(renamed, Desktop, "/lpc/it8689e/0/temperature/9", "Gone").Refused);
        Assert.Equal(renamed.SensorNames, Done(OverlayEdits.Reset(renamed)).SensorNames);

        var undone = history.Undo(fan)!;
        var first = history.Undo(undone.Config)!;
        Assert.Equal((File(renamed), File(start)), (File(undone.Config), File(first.Config)));
        Assert.Equal(File(fan), File(history.Redo(history.Redo(first.Config)!.Config)!.Config));
    }
}
