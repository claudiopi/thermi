using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Core;

/// <summary>
/// One sensor as the sensor host reports it, before thermi maps it to a metric.
/// <see cref="Id"/> is the source library's identifier, like "/amdcpu/0/temperature/2"; it can change
/// between library versions, so only the user's config may name it. <see cref="Type"/> is the library's
/// sensor type name, like "Temperature" or "Fan".
/// </summary>
public sealed record RawSensor(string Id, string HardwareType, string Hardware, string Type, string Name, double? Value);

/// <summary>Every sensor read at one moment.</summary>
public sealed record Snapshot(IReadOnlyList<RawSensor> Sensors);

/// <summary>
/// The pipe protocol: one snapshot per line, as JSON. Reading never throws: the line comes from
/// another process, so anything malformed or oversized is simply not a snapshot.
/// </summary>
public static class SnapshotJson
{
    public const string PipeName = "thermi-sensors";

    /// <summary>Far above a real snapshot (about 20 KB); a longer line is rejected unread.</summary>
    public const int MaxLineLength = 1_000_000;

    public static string Write(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, CoreJson.Default.Snapshot);

    public static Snapshot? Read(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLength)
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize(line, CoreJson.Default.Snapshot);
            return snapshot?.Sensors is not null && snapshot.Sensors.All(IsComplete) ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The generated reader lets a missing string through as null, whatever the type says.
    private static bool IsComplete(RawSensor? sensor) =>
        sensor is { Id: not null, HardwareType: not null, Hardware: not null, Type: not null, Name: not null };
}

// Sensors do report NaN and infinity; they travel as "NaN" and "Infinity" and show as missing.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(Snapshot))]
internal sealed partial class CoreJson : JsonSerializerContext;
