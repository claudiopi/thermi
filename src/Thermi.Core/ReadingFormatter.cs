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
            SensorKind.Temperature => string.Create(culture, $"{value:0}°"),
            SensorKind.Load => string.Create(culture, $"{value:0}%"),
            SensorKind.Clock => string.Create(culture, $"{value / 1000:0.0} GHz"),
            SensorKind.Power => string.Create(culture, $"{value:0} W"),
            SensorKind.Fan => string.Create(culture, $"{value:0} rpm"),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.Kind, "Unknown sensor kind."),
        };
    }
}
