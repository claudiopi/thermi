using System.Text.Json.Serialization;

namespace Thermi.Client;

/// <summary>
/// How thermi's sensors stand: the service, running, stopped or stale; whether an overlay runs and the install is the
/// sensors alone; and each problem with what a person does about it. A choice, as a problem's code, is the contract's word
/// for it, so a word a later thermi adds reads too.
/// </summary>
public sealed record ThermiStatus(string Service, bool Overlay, bool SensorsOnly, IReadOnlyList<ThermiProblem> Problems)
{
    public int Schema { get; init; }

    /// <summary>Why the settings file cannot be used, as broken, newer or locked, or null when it reads well.</summary>
    public string? Settings { get; init; }
}

/// <summary>A problem, as <c>service-stopped</c> or <c>driver-missing</c>, with its cause and the fix to tell a person.</summary>
public sealed record ThermiProblem(string Code, string Cause, string Fix);

/// <summary>What one read gave, or one line of a watch: each value asked for, and the ids that name nothing.</summary>
public sealed record ThermiReading(IReadOnlyList<ThermiValue> Values, IReadOnlyList<string> Unknown)
{
    public int Schema { get; init; }

    public DateTimeOffset? At { get; init; }
}

/// <summary>
/// A metric, a sensor or an overlay's item, in its fixed unit; a value that cannot be read is null, with why, never 0.
/// Over many samples, their minimum, maximum and average; for an item, what the overlay shows and its level.
/// </summary>
public sealed record ThermiValue(string Id, string Source, string Name, string Type, string Unit)
{
    public string? Sensor { get; init; }

    public string? Device { get; init; }

    public double? Value { get; init; }

    /// <summary>A value read as text, as a network address.</summary>
    public string? Text { get; init; }

    public string? Why { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    public double? Average { get; init; }

    public int Samples { get; init; }

    public int? Overlay { get; init; }

    public string? Shown { get; init; }

    public string? Level { get; init; }

    public double? Warning { get; init; }

    public double? Critical { get; init; }
}

/// <summary>Every sensor the service reads, by device.</summary>
public sealed record ThermiSensors(IReadOnlyList<ThermiDevice> Devices)
{
    public int Schema { get; init; }
}

public sealed record ThermiDevice(string Id, string Name, string? Kind, IReadOnlyList<ThermiSensor> Sensors);

public sealed record ThermiSensor(string Id, string Name, string Type, string Unit)
{
    /// <summary>The name the sensor library gives it, when thermi names it otherwise.</summary>
    public string? Library { get; init; }

    public double? Value { get; init; }

    public string? Text { get; init; }

    public string? Why { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ThermiStatus))]
[JsonSerializable(typeof(ThermiReading))]
[JsonSerializable(typeof(ThermiSensors))]
internal sealed partial class ThermiJson : JsonSerializerContext;
