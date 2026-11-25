using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Look;

/// <summary>The units a user reads beside a value whose words differ between languages.</summary>
[Translated]
public enum UnitText
{
    BytesPerSecond,
    KilobytesPerSecond,
    MegabytesPerSecond,
    GigabytesPerSecond,
    BitsPerSecond,
    KilobitsPerSecond,
    MegabitsPerSecond,
    GigabitsPerSecond,
    Hours,

    /// <summary>A time of an hour or more, its hours and minutes in the blanks.</summary>
    HoursMinutes,

    /// <summary>A time under an hour, its minutes in the blank.</summary>
    Minutes,
}

/// <summary>
/// How values and their words show: in the language of <see cref="Texts"/>, numbers in the regional format,
/// temperatures in the chosen unit.
/// </summary>
public sealed record ValueFormat(Texts Texts, IFormatProvider Numbers, TemperatureUnit Temperature = TemperatureUnit.Celsius)
{
    /// <summary>The sensors known now, which name an item that stores no name of its own as its sensor is named.</summary>
    public Snapshot? Known { get; init; }
}

/// <summary>Turns a reading into the short text the overlay shows.</summary>
public static class ReadingFormatter
{
    /// <summary>Shown when a sensor has no usable value. Never a zero: a zero looks like a real reading.</summary>
    public const string Missing = "–";

    // A throughput's scales, largest first, with 1 KB = 1000 B, and the letter each keeps when units are hidden.
    private static readonly (double Size, string Letter, UnitText Unit)[] Rates =
    [
        (1e9, "G", UnitText.GigabytesPerSecond),
        (1e6, "M", UnitText.MegabytesPerSecond),
        (1e3, "K", UnitText.KilobytesPerSecond),
    ];

    private static readonly (double Size, string Letter, UnitText Unit)[] BitRates =
    [
        (1e9, "G", UnitText.GigabitsPerSecond),
        (1e6, "M", UnitText.MegabitsPerSecond),
        (1e3, "K", UnitText.KilobitsPerSecond),
    ];

    /// <summary>
    /// The value in its unit, or the number alone when <paramref name="units"/> is false, a throughput keeping its scale's
    /// letter; a temperature in the chosen unit, with <paramref name="decimals"/> in place of its type's own when an item
    /// asks for them, and words of units in the language of <paramref name="texts"/>, English when there are none.
    /// </summary>
    public static string Format(
        Reading reading,
        IFormatProvider culture,
        bool units = true,
        TemperatureUnit temperature = TemperatureUnit.Celsius,
        int? decimals = null,
        Texts? texts = null)
    {
        if (reading.Usable is not { } value)
        {
            return Missing;
        }

        var language = texts ?? Texts.English;
        if (reading.Kind is SensorKind.Duration or SensorKind.Time or SensorKind.Address)
        {
            return reading.Kind == SensorKind.Duration ? Duration(value, culture, units, language)
                : reading.Kind == SensorKind.Time ? SystemSensors.TimeText(value, culture)
                : SystemSensors.AddressText(value);
        }

        var (number, own, unit, letter) = Parts(reading.Kind, value, temperature, language, reading.Bits);
        var text = number.ToString(reading.Kind is SensorKind.Fan or SensorKind.Data ? Grouped(decimals ?? own) : Digits(decimals ?? own), culture);
        return units ? string.Concat(text, " ", unit) : string.Concat(text, letter);
    }

    /// <summary>
    /// A value as the window names it, a meter's line or an item's offset, the number alone without <paramref name="units"/>;
    /// an offset is a <paramref name="difference"/>, so in °F it is 9/5 of the °C the file keeps, without the 32.
    /// </summary>
    public static string Format(SensorKind kind, double value, ValueFormat format, bool units = true, bool difference = false)
    {
        if (!difference || kind != SensorKind.Temperature)
        {
            return Format(new Reading(kind, value), format.Numbers, units, format.Temperature, texts: format.Texts);
        }

        var number = Format(new Reading(kind, Temperatures.ShownDifference(value, format.Temperature)), format.Numbers, units: false, texts: format.Texts);
        return units ? string.Concat(number, " ", UnitOf(kind, format.Temperature, format.Texts)) : number;
    }

    /// <summary>
    /// The widest text a kind shows in daily use. The overlay keeps this much room for each value, so it
    /// does not grow and shrink on the taskbar as the numbers change. Digits are all one width in the fonts
    /// it uses, so eights stand for any digits.
    /// </summary>
    public static string Widest(
        SensorKind kind,
        IFormatProvider culture,
        bool units = true,
        TemperatureUnit temperature = TemperatureUnit.Celsius,
        int? decimals = null,
        Texts? texts = null) => Format(
        new Reading(kind, kind switch
        {
            SensorKind.Fan or SensorKind.Clock => 8888,
            SensorKind.Power => 888,
            SensorKind.Data => 888.8,
            SensorKind.Temperature or SensorKind.Load or SensorKind.Control => 100,
            SensorKind.Throughput => 88.8e6,
            SensorKind.Duration => ((8 * 60) + 58) * 60,
            SensorKind.Time => (((22 * 60) + 58) * 60) + 58,
            SensorKind.Address => uint.MaxValue,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sensor kind."),
        }),
        culture,
        units,
        temperature,
        decimals,
        texts);

    /// <summary>The unit a type shows in, a temperature in the chosen one; a throughput's smallest.</summary>
    public static string UnitOf(SensorKind kind, TemperatureUnit temperature = TemperatureUnit.Celsius, Texts? texts = null) =>
        Parts(kind, 0, temperature, texts ?? Texts.English).Unit;

    /// <summary>
    /// The number a value shows as, how many decimals it shows, its unit, and the letter it keeps without units;
    /// a throughput in <paramref name="bits"/> per second when asked.
    /// </summary>
    internal static (double Number, int Decimals, string Unit, string Letter) Parts(
        SensorKind kind,
        double value,
        TemperatureUnit temperature,
        Texts texts,
        bool bits = false) => kind switch
        {
            SensorKind.Temperature when temperature == TemperatureUnit.Fahrenheit => (Temperatures.Shown(value, temperature), 0, "°F", ""),
            SensorKind.Temperature => (value, 0, "°C", ""),
            SensorKind.Load or SensorKind.Control => (value, 0, "%", ""),
            SensorKind.Clock => (value / 1000, 1, "GHz", ""),
            SensorKind.Power => (value, 0, "W", ""),
            SensorKind.Fan => (value, 0, "RPM", ""),
            SensorKind.Data => Data(value),
            SensorKind.Throughput when bits => Rate(value * 8, texts, BitRates, UnitText.BitsPerSecond),
            SensorKind.Throughput => Rate(value, texts, Rates, UnitText.BytesPerSecond),
            SensorKind.Duration => (value / 3600, 1, texts.Of(UnitText.Hours), ""),
            SensorKind.Time or SensorKind.Address => (value, 0, "", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sensor kind."),
        };

    /// <summary>A number's format without thousands separators.</summary>
    internal static string Digits(int decimals) => decimals == 0 ? "0" : "0." + new string('0', decimals);

    /// <summary>A time as hours and minutes, as 2:14, the way it shows without units.</summary>
    internal static string Clock(double seconds, IFormatProvider culture)
    {
        var minutes = (long)Math.Round(seconds / 60, MidpointRounding.AwayFromZero);
        return string.Create(culture, $"{minutes / 60}:{minutes % 60:00}");
    }

    // A fan's thousands are grouped, as 1,087 RPM, since its speed has four digits.
    private static string Grouped(int decimals) => "N" + decimals.ToString(CultureInfo.InvariantCulture);

    // A drive's counters run to tens of thousands of gigabytes: from what would read 1,000.0 GB on, they read in
    // terabytes with three significant digits, so a value never reads wider than 888.8 GB, the room it keeps.
    private static (double Number, int Decimals, string Unit, string Letter) Data(double gigabytes)
    {
        if (Math.Abs(gigabytes) < 999.95)
        {
            return (gigabytes, 1, "GB", "");
        }

        var terabytes = gigabytes / 1000;
        return (terabytes, Math.Abs(terabytes) >= 99.95 ? 0 : Math.Abs(terabytes) >= 9.995 ? 1 : 2, "TB", "T");
    }

    // The largest scale that keeps three significant digits, rounded first, so 999,999 B/s reads 1.00 MB/s and never 1000 KB/s.
    private static (double Number, int Decimals, string Unit, string Letter) Rate(
        double units,
        Texts texts,
        (double Size, string Letter, UnitText Unit)[] scales,
        UnitText smallest)
    {
        var digit = units == 0 ? 1 : Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(units))) - 2);
        var rounded = Math.Round(units / digit, MidpointRounding.AwayFromZero) * digit;
        if (Array.Find(scales, rate => Math.Abs(rounded) >= rate.Size) is not { Letter: not null } scale)
        {
            return (rounded, 0, texts.Of(smallest), "");
        }

        var number = rounded / scale.Size;
        return (number, Math.Abs(number) >= 100 ? 0 : Math.Abs(number) >= 10 ? 1 : 2, texts.Of(scale.Unit), scale.Letter);
    }

    private static string Duration(double seconds, IFormatProvider culture, bool units, Texts texts)
    {
        if (!units)
        {
            return Clock(seconds, culture);
        }

        var minutes = (long)Math.Round(seconds / 60, MidpointRounding.AwayFromZero);
        return minutes >= 60
            ? texts.Format(culture, UnitText.HoursMinutes, minutes / 60, minutes % 60)
            : texts.Format(culture, UnitText.Minutes, minutes);
    }
}

/// <summary>
/// Temperatures in the unit the person chose. The file always keeps °C, rounded to a tenth, so thresholds and
/// offsets mean the same whatever the unit; a threshold is a temperature, an offset a difference between two.
/// </summary>
public static class Temperatures
{
    /// <summary>Where a temperature meter's track starts and ends, and its step, in the chosen unit.</summary>
    public static (double From, double To, double Step) Track(TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? (32, 212, 1) : (0, 100, 1);

    public static double Shown(double celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? (celsius * 9 / 5) + 32 : celsius;

    /// <summary>A temperature in the chosen unit, as the file keeps it.</summary>
    public static double Kept(double shown, TemperatureUnit unit) =>
        Tenth(unit == TemperatureUnit.Fahrenheit ? (shown - 32) * 5 / 9 : shown);

    /// <summary>A difference of temperatures, as an offset: in °F, 9/5 of it, without the 32.</summary>
    public static double ShownDifference(double celsius, TemperatureUnit unit) =>
        unit == TemperatureUnit.Fahrenheit ? celsius * 9 / 5 : celsius;

    public static double KeptDifference(double shown, TemperatureUnit unit) =>
        Tenth(unit == TemperatureUnit.Fahrenheit ? shown * 5 / 9 : shown);

    private static double Tenth(double celsius) => Math.Round(celsius, 1, MidpointRounding.AwayFromZero);
}
