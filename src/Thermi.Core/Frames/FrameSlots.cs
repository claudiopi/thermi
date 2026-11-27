using System.Globalization;
using Thermi.Core.Sensors;

namespace Thermi.Core.Frames;

/// <summary>
/// A second's frame rates as the pipe carries them: fixed slots of sensors, a slot's process id, its parent's, its FPS, its
/// frame time and its two lows as values, and one sensor that says whether the service could read frames, or had no rights
/// to try. The slots never change their ids or names, so the pipe keeps sending values alone
/// (docs/adr/0012-sensor-pipe-catalog-once.md), and they never leave the reader: <see cref="GameSensors"/> turns them into
/// the game's own sensors. A service of an earlier version sends no lows, which then read as unknown, never as 0.
/// </summary>
public static class FrameSlots
{
    public const string HardwareType = "Frames";

    private const string Device = "/frames/0";

    // The state sensor's values: frames read, and a host with no rights to read them; none when they cannot be read.
    private const double ReadState = 1;
    private const double NoRightsState = 0;

    private static readonly string[] Parts = ["process", "parent", "rate", "wait", "low1", "low01"];

    /// <summary>
    /// The sensors of <paramref name="rates"/>, the busiest first, or of no rates at all when the service cannot read frames.
    /// </summary>
    public static IEnumerable<RawSensor> Of(IReadOnlyList<FrameRate>? rates) => Of(rates, rates is null ? null : ReadState);

    /// <summary>
    /// The sensors of a host with no rights to trace presents, the user's own in a per-user install
    /// (docs/adr/0016-admin-on-demand.md): frames read only once thermi is installed for everyone.
    /// </summary>
    public static IEnumerable<RawSensor> WithoutRights() => Of(null, NoRightsState);

    /// <summary>Whether <paramref name="snapshot"/> came from a host with no rights to trace presents.</summary>
    public static bool NeedRights(Snapshot snapshot) => State(snapshot) is NoRightsState;

    private static IEnumerable<RawSensor> Of(IReadOnlyList<FrameRate>? rates, double? state)
    {
        yield return Sensor("state", 0, state);
        for (var slot = 0; slot < FrameTally.Slots; slot++)
        {
            FrameRate? rate = rates is not null && slot < rates.Count ? rates[slot] : null;
            yield return Sensor(Parts[0], slot, rate?.Process);
            yield return Sensor(Parts[1], slot, rate?.Parent);
            yield return Sensor(Parts[2], slot, rate?.Fps);
            yield return Sensor(Parts[3], slot, rate?.FrameTime);
            yield return Sensor(Parts[4], slot, rate?.OnePercentLow);
            yield return Sensor(Parts[5], slot, rate?.PointOnePercentLow);
        }
    }

    /// <summary>The rates a snapshot carries, or null when its service could not read frames, or sent no slots.</summary>
    public static IReadOnlyList<FrameRate>? Read(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = snapshot.Sensors.Where(Is).ToDictionary(sensor => sensor.Id, sensor => sensor.Value, StringComparer.Ordinal);
        if (values.GetValueOrDefault(Id("state", 0)) is not ReadState)
        {
            return null;
        }

        var rates = new List<FrameRate>();
        for (var slot = 0; slot < FrameTally.Slots; slot++)
        {
            if (Value(values, 0, slot) is { } process && Value(values, 2, slot) is { } fps && Value(values, 3, slot) is { } frameTime)
            {
                rates.Add(new FrameRate((int)process, (int)(Value(values, 1, slot) ?? 0), fps, frameTime, Value(values, 4, slot), Value(values, 5, slot)));
            }
        }

        return rates;
    }

    public static bool Is(RawSensor sensor) => sensor.HardwareType == HardwareType;

    private static double? State(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var id = Id("state", 0);
        return snapshot.Sensors.FirstOrDefault(sensor => Is(sensor) && sensor.Id == id)?.Value;
    }

    private static double? Value(Dictionary<string, double?> values, int part, int slot) =>
        values.GetValueOrDefault(Id(Parts[part], slot)) is { } value && double.IsFinite(value) ? value : null;

    private static string Id(string part, int slot) => string.Create(CultureInfo.InvariantCulture, $"{Device}/{part}/{slot}");

    private static RawSensor Sensor(string part, int slot, double? value) =>
        new(Id(part, slot), HardwareType, HardwareType, part, string.Create(CultureInfo.InvariantCulture, $"{part} {slot}"), value);
}
