using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Agents;

/// <summary>
/// What <c>thermi-cli</c> answers, the contract other programs read (<c>docs/adr/0013-agents-cli-and-mcp.md</c>): each
/// answer says its schema, <see cref="Current"/> today, fields are only ever added to one, and a value reads in a fixed unit per type,
/// a temperature in °C whatever the overlay shows.
/// </summary>
public static class AgentSchema
{
    public const int Current = 1;
}

/// <summary>What an id read names: one of the overlay's metrics, a sensor of the service, or an item of the settings.</summary>
public enum ValueSource
{
    Metric,
    Sensor,
    Item,
}

/// <summary>
/// One value read: <see cref="Value"/> in <see cref="Unit"/>, the last of the samples, or null with <see cref="Why"/>; its
/// minimum, maximum and average over the <see cref="Samples"/> that read, when more than one was taken; for an item, its
/// text and level as the overlay shows them and the lines that judge it. An address reads as <see cref="Text"/> alone.
/// </summary>
public sealed record ValueAnswer(string Id, ValueSource Source, string Name, SensorKind Type, string Unit)
{
    /// <summary>The sensor it reads, for a metric or an item of one sensor.</summary>
    public string? Sensor { get; init; }

    public string? Device { get; init; }

    public double? Value { get; init; }

    /// <summary>A value that is no number, as an IPv4 address.</summary>
    public string? Text { get; init; }

    public NoValue? Why { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Average { get; init; }

    public int Samples { get; init; }

    /// <summary>The overlay an item is in, counted from 0 as the settings list them.</summary>
    public int? Overlay { get; init; }

    /// <summary>
    /// This moment's reading as the overlay writes it, in its unit and decimals and the invariant culture's numbers; an
    /// item that shows a minimum, maximum, average or smoothed value over its own history shows here the reading it is made of.
    /// </summary>
    public string? Shown { get; init; }

    public Level? Level { get; init; }

    public double? Warning { get; init; }

    public double? Critical { get; init; }
}

/// <summary>A read: each value asked for, in the order asked, and the ids that name nothing.</summary>
public sealed record ReadAnswer(IReadOnlyList<ValueAnswer> Values, IReadOnlyList<string> Unknown)
{
    public int Schema { get; init; } = AgentSchema.Current;

    /// <summary>When the last sample was read.</summary>
    public DateTimeOffset? At { get; init; }
}
