using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Tests;

/// <summary>What the overlay shows for a reading, including the ways real sensors fail.</summary>
public sealed class ReadingTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, 61.6, "62 °C")]
    [InlineData(SensorKind.Load, 7.2, "7 %")]
    [InlineData(SensorKind.Clock, 4845.0, "4.8 GHz")]
    [InlineData(SensorKind.Power, 88.4, "88 W")]
    [InlineData(SensorKind.Fan, 1450.0, "1,450 RPM")]
    [InlineData(SensorKind.Data, 999.94, "999.9 GB")]
    [InlineData(SensorKind.Data, 999.95, "1.00 TB")]
    [InlineData(SensorKind.Data, 60987.0, "61.0 TB")]
    [InlineData(SensorKind.Fan, double.NaN, ReadingFormatter.Missing)]
    [InlineData(SensorKind.Load, -3.0, "0 %")]
    [InlineData(SensorKind.Control, 104.2, "100 %")]
    [InlineData(SensorKind.Throughput, 0.0, "0 B/s")]
    [InlineData(SensorKind.Throughput, 999.0, "999 B/s")]
    [InlineData(SensorKind.Throughput, 1000.0, "1.00 KB/s")]
    [InlineData(SensorKind.Throughput, 812_000.0, "812 KB/s")]
    [InlineData(SensorKind.Throughput, 12_400_000.0, "12.4 MB/s")]
    [InlineData(SensorKind.Throughput, 1_050_000_000.0, "1.05 GB/s")]
    [InlineData(SensorKind.Throughput, 999_999.0, "1.00 MB/s")]
    [InlineData(SensorKind.Throughput, 12_400_000.0, "12,4 MB/s", "es-UY")]
    [InlineData(SensorKind.Throughput, 12_400_000.0, "12.4M", "en-US", "bare")]
    [InlineData(SensorKind.Throughput, 999.0, "999", "en-US", "bare")]
    [InlineData(SensorKind.Throughput, 812_000.0, "812K", "en-US", "tray")]
    [InlineData(SensorKind.Throughput, 1_050_000_000.0, "1.1G", "en-US", "tray")]
    [InlineData(SensorKind.Power, -12.4, "-12.4 W", "en-US", "tenth")]
    [InlineData(SensorKind.Power, 12.4, "12,4 W", "es-UY", "tenth")]
    [InlineData(SensorKind.Duration, 8040.0, "2 h 14 min")]
    [InlineData(SensorKind.Duration, 840.0, "14 min")]
    [InlineData(SensorKind.Duration, 8040.0, "2:14", "en-US", "tray")]
    [InlineData(SensorKind.Duration, null, ReadingFormatter.Missing)]
    [InlineData(SensorKind.Throughput, 12_400_000.0, "99.2 Mbps", "en-US", "bits")]
    [InlineData(SensorKind.Throughput, 100.0, "800 bps", "en-US", "bits")]
    [InlineData(SensorKind.Throughput, 125_000_000.0, "1.00 Gbps", "en-US", "bits")]
    [InlineData(SensorKind.Throughput, 12_400_000.0, "99M", "en-US", "tray bits")]
    [InlineData(SensorKind.Time, 50709.0, "2:05:09 PM")]
    [InlineData(SensorKind.Time, 50709.0, "14:05:09", "es-UY")]
    [InlineData(SensorKind.Address, 3232235799.0, "192.168.1.23")]
    [InlineData(SensorKind.Address, 3232235799.0, ".23", "en-US", "tray")]
    public void FormatsEachKindAndShowsBrokenReadingsAsMissing(SensorKind kind, double? value, string expected, string culture = "en-US", string form = "")
    {
        var reading = new Reading(kind, value) { Bits = form.Contains("bits", StringComparison.Ordinal) };
        var numbers = CultureInfo.GetCultureInfo(culture);

        var text = form.Replace(" bits", "", StringComparison.Ordinal) switch
        {
            "bare" => ReadingFormatter.Format(reading, numbers, units: false),
            "tray" => TrayTexts.IconText(reading, numbers),
            "tenth" => ReadingFormatter.Format(reading, numbers, decimals: 1),
            _ => ReadingFormatter.Format(reading, numbers),
        };

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(79.4, Level.Normal)]
    [InlineData(79.5, Level.Warning)]
    [InlineData(95.0, Level.Critical)]
    [InlineData(double.NaN, Level.Normal)]
    public void LevelsFollowTheThresholdAndMissingIsNeverAlarming(double? value, Level expected)
    {
        var threshold = new Threshold(warning: 80, critical: 90);

        Assert.Equal(expected, threshold.LevelOf(new Reading(SensorKind.Temperature, value)));
    }

    [Theory]
    [InlineData(double.NaN, 90)]
    [InlineData(80, double.PositiveInfinity)]
    public void RejectsThresholdsThatCannotWork(double warning, double critical)
    {
        Assert.Throws<ArgumentException>(() => new Threshold(warning, critical));
    }
}
