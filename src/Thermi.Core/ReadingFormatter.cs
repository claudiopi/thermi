namespace Thermi.Core;

/// <summary>Turns a reading into the short text the overlay shows.</summary>
public static class ReadingFormatter
{
    /// <summary>Shown when a sensor has no usable value. Never a zero: a zero looks like a real reading.</summary>
    public const string Missing = "–";

    public static string Format(Reading reading, IFormatProvider culture)
    {
        if (reading.Usable is not { } value)
        {
            return Missing;
        }

        return reading.Kind switch
        {
            SensorKind.Temperature => string.Create(culture, $"{value:0} °C"),
            SensorKind.Load or SensorKind.Control => string.Create(culture, $"{value:0} %"),
            SensorKind.Clock => string.Create(culture, $"{value / 1000:0.0} GHz"),
            SensorKind.Power => string.Create(culture, $"{value:0} W"),
            SensorKind.Fan => string.Create(culture, $"{value:N0} RPM"),
            SensorKind.Data => string.Create(culture, $"{value:0.0} GB"),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.Kind, "Unknown sensor kind."),
        };
    }

    /// <summary>
    /// The widest text a kind shows in daily use. The overlay keeps this much room for each value, so it
    /// does not grow and shrink on the taskbar as the numbers change. Digits are all one width in the fonts
    /// it uses, so eights stand for any digits.
    /// </summary>
    public static string Widest(SensorKind kind, IFormatProvider culture) => Format(
        new Reading(kind, kind switch
        {
            SensorKind.Fan or SensorKind.Clock => 8888,
            SensorKind.Power => 888,
            SensorKind.Data => 888.8,
            SensorKind.Temperature or SensorKind.Load or SensorKind.Control => 100,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sensor kind."),
        }),
        culture);
}
