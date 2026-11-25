using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Thermi.Core.Sensors;

/// <summary>
/// One sensor as the sensor host reports it, before thermi maps it to a metric.
/// <see cref="Id"/> is the source library's identifier, like "/amdcpu/0/temperature/2"; it can change
/// between library versions, so only the user's config may name it. <see cref="Type"/> is the library's
/// sensor type name, like "Temperature" or "Fan". A <see cref="Value"/> of null, a sensor that reads nothing, is always
/// written, as the recordings and the window's page read it.
/// </summary>
public sealed record RawSensor(
    string Id,
    string HardwareType,
    string Hardware,
    string Type,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Value);

/// <summary>Every sensor read at one moment.</summary>
public sealed record Snapshot(IReadOnlyList<RawSensor> Sensors)
{
    /// <summary>
    /// The device ids of the network adapters that are not physical: loopback, tunnels and VPNs, and any Windows does not
    /// mark as a hardware interface, as Hyper-V's and WSL's switches. The service tells them by Windows' own flags, never by
    /// names, which vendors choose and Windows translates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? VirtualAdapters { get; init; }
}

/// <summary>
/// A snapshot as one line of JSON, as recordings hold it and as the pipe sends it whole, and its values alone, a JSON
/// array in the order of the whole one the pipe sent last (docs/adr/0012-sensor-pipe-catalog-once.md).
/// Reading never throws: the line comes from another process, so anything malformed or oversized is simply not a snapshot.
/// </summary>
public static class SnapshotJson
{
    public const string PipeName = "thermi-sensors";

    /// <summary>Far above a real snapshot (about 40 KB with network and disks); a longer line is rejected unread.</summary>
    public const int MaxLineLength = 1_000_000;

    public static string Write(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, CoreJson.Default.Snapshot);

    /// <summary>Adds <paramref name="snapshot"/> to <paramref name="line"/> as the pipe carries it, one line of UTF-8, with no string between.</summary>
    public static void WriteLine(Snapshot snapshot, IBufferWriter<byte> line)
    {
        ArgumentNullException.ThrowIfNull(line);
        using (var writer = new Utf8JsonWriter(line))
        {
            JsonSerializer.Serialize(writer, snapshot, CoreJson.Default.Snapshot);
        }

        line.Write("\n"u8);
    }

    public static Snapshot? Read(string? line) =>
        string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLength ? null : Read(Encoding.UTF8.GetBytes(line));

    /// <summary>A whole snapshot from one line of UTF-8, as the pipe carries it.</summary>
    public static Snapshot? Read(ReadOnlySpan<byte> line)
    {
        if (line.Length > MaxLineLength)
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

    /// <summary>Adds the values of <paramref name="snapshot"/> to <paramref name="line"/>, in its sensors' order, as one line.</summary>
    public static void WriteValues(Snapshot snapshot, IBufferWriter<byte> line)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(line);
        using (var writer = new Utf8JsonWriter(line))
        {
            writer.WriteStartArray();
            foreach (var sensor in snapshot.Sensors)
            {
                JsonSerializer.Serialize(writer, sensor.Value, CoreJson.Default.NullableDouble);
            }

            writer.WriteEndArray();
        }

        line.Write("\n"u8);
    }

    /// <summary>The values of a line <see cref="WriteValues"/> wrote, or null for anything else.</summary>
    public static double?[]? ReadValues(ReadOnlySpan<byte> line)
    {
        if (line.Length > MaxLineLength)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, CoreJson.Default.NullableDoubleArray);
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
[JsonSerializable(typeof(double?[]))]
[JsonSerializable(typeof(Frames.Present))]
internal sealed partial class CoreJson : JsonSerializerContext;
