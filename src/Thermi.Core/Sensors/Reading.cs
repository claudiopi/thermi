using Thermi.Core.Translations;

namespace Thermi.Core.Sensors;

/// <summary>
/// What a sensor measures, and the unit its value comes in. Each name is the sensor type a snapshot reports, but for the
/// library's "Level", a percentage read as a load, and its "TimeSpan", read as a duration.
/// </summary>
[Translated]
public enum SensorKind
{
    /// <summary>Degrees Celsius.</summary>
    Temperature,

    /// <summary>Percent, 0 to 100.</summary>
    Load,

    /// <summary>Megahertz.</summary>
    Clock,

    /// <summary>Watts.</summary>
    Power,

    /// <summary>Revolutions per minute.</summary>
    Fan,

    /// <summary>Percent of full speed a fan is driven at, 0 to 100.</summary>
    Control,

    /// <summary>Gigabytes.</summary>
    Data,

    /// <summary>Bytes per second, as a network's or a drive's speed.</summary>
    Throughput,

    /// <summary>Seconds, as a battery's time remaining or Windows' uptime.</summary>
    Duration,

    /// <summary>A time of day, in seconds since local midnight.</summary>
    Time,

    /// <summary>An IPv4 address, held as its 32-bit number, the first byte highest.</summary>
    Address,

    /// <summary>Frames per second the app in front presents; the one kind whose trouble is a low value.</summary>
    FrameRate,

    /// <summary>Milliseconds, as the longest wait for a frame in the last second.</summary>
    FrameTime,

    /// <summary>Volts, as a CPU's core or a board's +12 V.</summary>
    Voltage,

    /// <summary>Milliseconds a reply took to come back, as a ping's round trip.</summary>
    Latency,
}

/// <summary>Which way a network speed goes, drawn as an arrow before its value in every language.</summary>
public enum Direction
{
    None,
    Down,
    Up,
}

/// <summary>
/// One value read from a sensor. <see cref="Value"/> is null when the sensor exists but has
/// nothing to report; real hardware also reports NaN and infinities, and both mean the same.
/// </summary>
public readonly record struct Reading(SensorKind Kind, double? Value)
{
    /// <summary>Which way the speed it reads goes, for a network's download or upload.</summary>
    public Direction Direction { get; init; }

    /// <summary>Whether a throughput shows in bits per second, as its item asks; the value stays in bytes.</summary>
    public bool Bits { get; init; }

    /// <summary>
    /// The value, or null when the reading is missing or not a finite number. A percentage is held between 0 and 100, since
    /// sensors report loads a little past either end, so nothing shown, kept or charted from it ever leaves that range.
    /// </summary>
    public double? Usable => Value is { } value && double.IsFinite(value)
        ? Kind is SensorKind.Load or SensorKind.Control ? Math.Clamp(value, 0, 100) : value
        : null;
}
