using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>The voltages the library reads, listed, found and added as any sensor, with no lines.</summary>
public sealed class VoltageTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    // The recorded desktop with two of its board's voltages, named and placed as the library gives them.
    private static Snapshot Desktop
    {
        get
        {
            var desktop = TestRecordings.Read(TestRecordings.Admin);
            return desktop with
            {
                Sensors =
                [
                    .. desktop.Sensors,
                    new RawSensor("/lpc/it8689e/0/voltage/0", "SuperIO", "ITE IT8689E", "Voltage", "Vcore", 1.2481),
                    new RawSensor("/lpc/it8689e/0/voltage/1", "SuperIO", "ITE IT8689E", "Voltage", "+12V", 12.096),
                ],
            };
        }
    }

    // A8 of spec 005.
    [Fact]
    public void VoltagesAreListedFoundAndAddedWithoutLines()
    {
        var snapshot = Desktop;
        var devices = SensorCatalog.Build(snapshot);
        IReadOnlyList<string> Found(string search, SensorKind[] kinds)
        {
            return [.. SensorCatalog.Filter(devices, search, kinds).SelectMany(device => device.Sensors).Select(row => row.Id)];
        }

        string[] voltages = ["/lpc/it8689e/0/voltage/0", "/lpc/it8689e/0/voltage/1"];
        Assert.Equal(voltages, Found("", [SensorKind.Voltage]));
        Assert.Equal(voltages, Found("volts", []));
        Assert.Equal([voltages[0]], Found("vcore", []));
        Assert.All(
            devices.Single(device => device.Id == "/lpc/it8689e/0").Sensors.Where(row => row.Reading.Kind == SensorKind.Voltage),
            row => Assert.Contains(row.Id, voltages));
        Assert.Equal(
            ["1.248 V", "12.10 V"],
            voltages.Select(id => ReadingFormatter.Format(SensorMap.ReadingOf(SensorMap.Find(snapshot, id)!)!.Value, English)));

        var config = Edited.Done(ItemEdits.AddSensor(ThermiConfig.New, 0, snapshot, voltages[0]));
        var item = Edited.Item(config, voltages[0]);
        var shown = OverlayLayout.Build(config.Overlays[0], SensorMap.Read(snapshot, config.Overlays[0]), English);
        Assert.Equal("1.248 V", shown.Single(each => each.Id == item.Id).Value);
        Assert.Null(item.Threshold);
        Assert.False(Threshold.Judges(item));
        Assert.Equal((0.0, 0.0), PowerEdits.OffsetRange(SensorKind.Voltage));

        Assert.All(
            [.. ThermiConfig.New.Overlays, new OverlayConfig()],
            overlay => Assert.DoesNotContain(SensorKind.Voltage, SensorMap.Read(snapshot, overlay).Values.Select(reading => reading.Kind)));
    }
}
