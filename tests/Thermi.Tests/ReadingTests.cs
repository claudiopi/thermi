using System.Globalization;
using Thermi.Core;

namespace Thermi.Tests;

/// <summary>What the overlay shows for a reading, including the ways real sensors fail.</summary>
public sealed class ReadingTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-UY");

    [Theory]
    [InlineData(SensorKind.Temperature, 61.6, "62°")]
    [InlineData(SensorKind.Load, 7.2, "7%")]
    [InlineData(SensorKind.Clock, 4845.0, "4.8 GHz")]
    [InlineData(SensorKind.Power, 88.4, "88 W")]
    [InlineData(SensorKind.Fan, 1450.0, "1450 rpm")]
    [InlineData(SensorKind.Temperature, null, ReadingFormatter.Missing)]
    [InlineData(SensorKind.Fan, double.NaN, ReadingFormatter.Missing)]
    [InlineData(SensorKind.Power, double.PositiveInfinity, ReadingFormatter.Missing)]
    public void FormatsEachKindAndShowsBrokenReadingsAsMissing(SensorKind kind, double? value, string expected)
    {
        Assert.Equal(expected, ReadingFormatter.Format(new Reading(kind, value), English));
    }

    [Fact]
    public void FormatsWithTheUserCulture()
    {
        Assert.Equal("4,8 GHz", ReadingFormatter.Format(new Reading(SensorKind.Clock, 4845.0), Spanish));
    }

    [Fact]
    public void RejectsAnUnknownKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadingFormatter.Format(new Reading((SensorKind)99, 1), English));
    }

    [Theory]
    [InlineData(79.9, Level.Normal)]
    [InlineData(80.0, Level.Warning)]
    [InlineData(95.0, Level.Critical)]
    [InlineData(null, Level.Normal)]
    [InlineData(double.NaN, Level.Normal)]
    public void LevelsFollowTheThresholdAndMissingIsNeverAlarming(double? value, Level expected)
    {
        var threshold = new Threshold(warning: 80, critical: 90);

        Assert.Equal(expected, threshold.LevelOf(new Reading(SensorKind.Temperature, value)));
    }

    [Theory]
    [InlineData(90, 80)]
    [InlineData(double.NaN, 90)]
    [InlineData(80, double.PositiveInfinity)]
    public void RejectsThresholdsThatCannotWork(double warning, double critical)
    {
        Assert.Throws<ArgumentException>(() => new Threshold(warning, critical));
    }
}
