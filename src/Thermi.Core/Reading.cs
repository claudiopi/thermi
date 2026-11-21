namespace Thermi.Core;

/// <summary>What a sensor measures, and the unit its value comes in.</summary>
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
}

/// <summary>
/// One value read from a sensor. <see cref="Value"/> is null when the sensor exists but has
/// nothing to report; real hardware also reports NaN and infinities, and both mean the same.
/// </summary>
public readonly record struct Reading(SensorKind Kind, double? Value)
{
    /// <summary>The value, or null when the reading is missing or not a finite number.</summary>
    public double? Usable => Value is { } value && double.IsFinite(value) ? value : null;
}
