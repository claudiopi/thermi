using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>The voltages the library reads, listed, found and added as any sensor, with no lines.</summary>
public sealed class VoltageTests
{
    private const string Board = "/lpc/it8689e/0";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static readonly SystemFacts Facts = new(TimeSpan.Zero, null, DateTimeOffset.UnixEpoch);

    // A8 of spec 005: the recorded desktop as administrator, and without admin rights, as the readers take them.
    [Fact]
    public void VoltagesAreListedFoundAndAddedWithoutLines()
    {
        var admin = SystemSensors.With(TestRecordings.Read(TestRecordings.AdminVoltages), Facts);
        var devices = SensorCatalog.Build(admin);
        IReadOnlyList<string> Found(string search, SensorKind[] kinds)
        {
            return [.. SensorCatalog.Filter(devices, search, kinds).SelectMany(device => device.Sensors).Select(row => row.Id)];
        }

        string[] board = [.. Enumerable.Range(0, 9).Select(input => $"{Board}/voltage/{input}")];
        Assert.Equal(28, Found("", [SensorKind.Voltage]).Count);
        Assert.Equal(Found("", [SensorKind.Voltage]), Found("volts", []));
        Assert.Equal([$"{Board}/voltage/7"], Found("standby", []));
        Assert.Equal(board, devices.Single(device => device.Id == Board).Sensors.Where(row => row.Reading.Kind == SensorKind.Voltage).Select(row => row.Id));
        Assert.Equal(
            ["1.296 V", "3.336 V", "0.306 V", "1.045 V", "12.10 V"],
            new[] { board[0], board[7], "/amdcpu/0/voltage/2", "/gpu-nvidia/0/voltage/0" }
                .Select(id => SensorMap.ReadingOf(SensorMap.Find(admin, id)!)!.Value)
                .Append(new Reading(SensorKind.Voltage, 12.096))
                .Select(reading => ReadingFormatter.Format(reading, English)));

        var config = Edited.Done(ItemEdits.AddSensor(ThermiConfig.New, 0, admin, board[0]));
        var item = Edited.Item(config, board[0]);
        var shown = OverlayLayout.Build(config.Overlays[0], SensorMap.Read(admin, config.Overlays[0]), English);
        Assert.Equal("1.296 V", shown.Single(each => each.Id == item.Id).Value);
        Assert.Null(item.Threshold);
        Assert.False(Threshold.Judges(item));
        Assert.Equal((0.0, 0.0), PowerEdits.OffsetRange(SensorKind.Voltage));
        Assert.Equal(Refusal.SumOfVoltages, PowerEdits.AddCombined(ThermiConfig.New, 0, admin, board[..2], Combine.Sum).Refused);
        Assert.Null(SensorMap.ReadingOf(new RawSensor("/amdcpu/0/voltage/0", "Cpu", "AMD Ryzen 9 9950X", "Voltage", "Core", 0))!.Value.Value);
        Assert.All(
            [.. ThermiConfig.New.Overlays, new OverlayConfig()],
            overlay => Assert.DoesNotContain(SensorKind.Voltage, SensorMap.Read(admin, overlay).Values.Select(reading => reading.Kind)));

        var rows = SensorCatalog.Build(SystemSensors.With(TestRecordings.Read(TestRecordings.NoAdminVoltages), Facts))
            .SelectMany(device => device.Sensors).Where(row => row.Reading.Kind == SensorKind.Voltage).ToList();
        Assert.Equal(19, rows.Count);
        Assert.All(rows.Where(row => row.Id.StartsWith("/amdcpu/", StringComparison.Ordinal)), row => Assert.Equal(NoValue.DriverMayBeMissing, row.Why));
        Assert.All(rows.Where(row => row.Id.StartsWith("/gpu-", StringComparison.Ordinal)), row => Assert.Null(row.Why));
    }
}
