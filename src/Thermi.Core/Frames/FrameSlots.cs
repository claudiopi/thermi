using System.Globalization;
using Thermi.Core.Sensors;

namespace Thermi.Core.Frames;

/// <summary>
/// A second's frame rates as the pipe carries them: fixed slots of sensors, a slot's process id, its parent's, its FPS and
/// its frame time as values, and one sensor that says the service could read frames at all. The slots never change their
/// ids or names, so the pipe keeps sending values alone (docs/adr/0012-sensor-pipe-catalog-once.md), and they never leave
/// the reader: <see cref="GameSensors"/> turns them into the game's own sensors.
/// </summary>
public static class FrameSlots
{
    public const string HardwareType = "Frames";

    private const string Device = "/frames/0";

    private static readonly string[] Parts = ["process", "parent", "rate", "wait"];

    /// <summary>
    /// The sensors of <paramref name="rates"/>, the busiest first, or of no rates at all when the service cannot read frames.
    /// </summary>
    public static IEnumerable<RawSensor> Of(IReadOnlyList<FrameRate>? rates)
    {
        yield return Sensor("state", 0, rates is null ? null : 1);
        for (var slot = 0; slot < FrameTally.Slots; slot++)
        {
            FrameRate? rate = rates is not null && slot < rates.Count ? rates[slot] : null;
            yield return Sensor(Parts[0], slot, rate?.Process);
            yield return Sensor(Parts[1], slot, rate?.Parent);
            yield return Sensor(Parts[2], slot, rate?.Fps);
            yield return Sensor(Parts[3], slot, rate?.FrameTime);
        }
    }

    /// <summary>The rates a snapshot carries, or null when its service could not read frames, or sent no slots.</summary>
    public static IReadOnlyList<FrameRate>? Read(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = snapshot.Sensors.Where(Is).ToDictionary(sensor => sensor.Id, sensor => sensor.Value, StringComparer.Ordinal);
        if (values.GetValueOrDefault(Id("state", 0)) is not 1)
        {
            return null;
        }

        var rates = new List<FrameRate>();
        for (var slot = 0; slot < FrameTally.Slots; slot++)
        {
            if (Value(values, 0, slot) is { } process && Value(values, 2, slot) is { } fps && Value(values, 3, slot) is { } frameTime)
            {
                rates.Add(new FrameRate((int)process, (int)(Value(values, 1, slot) ?? 0), fps, frameTime));
            }
        }

        return rates;
    }

    public static bool Is(RawSensor sensor) => sensor.HardwareType == HardwareType;

    private static double? Value(Dictionary<string, double?> values, int part, int slot) =>
        values.GetValueOrDefault(Id(Parts[part], slot)) is { } value && double.IsFinite(value) ? value : null;

    private static string Id(string part, int slot) => string.Create(CultureInfo.InvariantCulture, $"{Device}/{part}/{slot}");

    private static RawSensor Sensor(string part, int slot, double? value) =>
        new(Id(part, slot), HardwareType, HardwareType, part, string.Create(CultureInfo.InvariantCulture, $"{part} {slot}"), value);
}
