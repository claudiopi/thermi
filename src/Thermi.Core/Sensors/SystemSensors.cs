using System.Globalization;
using Thermi.Core.Settings;

namespace Thermi.Core.Sensors;

/// <summary>
/// What Windows itself says, with no sensor: how long it has run, the IPv4 address its default route goes out from, if any,
/// and the local time now.
/// </summary>
public sealed record WindowsFacts(TimeSpan Uptime, uint? Address, DateTimeOffset LocalNow);

/// <summary>
/// The sensors of a device of thermi's own, the PC, that both processes add to every snapshot they read: so the catalog,
/// the items, their names and the history take them as any sensor, and the pipe does not change.
/// </summary>
public static class SystemSensors
{
    public const string HardwareType = "System";

    public const string Uptime = "Uptime";

    public const string LocalIp = "Local IP";

    public const string Clock = "Clock";

    private const string Device = "/system/0";

    private const string Hardware = "PC";

    private const int SecondsPerDay = 24 * 60 * 60;

    public static Snapshot With(Snapshot snapshot, WindowsFacts facts) => snapshot with
    {
        Sensors =
        [
            .. snapshot.Sensors,
            Sensor(SensorKind.Duration, "duration", Uptime, Math.Floor(facts.Uptime.TotalSeconds)),
            Sensor(SensorKind.Address, "address", LocalIp, facts.Address),
            Sensor(SensorKind.Time, "time", Clock, Math.Floor(facts.LocalNow.TimeOfDay.TotalSeconds)),
        ],
    };

    /// <summary>The PC's own sensors of a snapshot, which hold while the service sends nothing, or none without one.</summary>
    public static Snapshot Only(Snapshot? snapshot) =>
        snapshot is null ? new Snapshot([]) : snapshot with { Sensors = [.. snapshot.Sensors.Where(IsOwn)] };

    /// <summary>
    /// Whether a snapshot holds anything the sensor service read, beyond what Windows itself says and the game's sensors,
    /// which every reader adds.
    /// </summary>
    public static bool HasServiceSensors(Snapshot snapshot) => snapshot.Sensors.Any(sensor => !IsOwn(sensor) && !Frames.GameSensors.Is(sensor));

    /// <summary>Whether an item reads only what Windows itself says, so it never waits on the sensor service.</summary>
    public static bool Reads(ItemConfig item)
    {
        IReadOnlyList<string> ids = item.Sensor is { } one ? [one] : item.Sensors ?? [];
        return ids.Count > 0
            ? ids.All(id => id.StartsWith(Device + "/", StringComparison.Ordinal))
            : Metrics.Find(item.Metric ?? item.Auto) is { } metric && Metrics.DeviceOf(metric) == DeviceKind.System;
    }

    /// <summary>An IPv4 address's four bytes as one number, the first highest, as a snapshot carries it.</summary>
    public static uint AddressOf(ReadOnlySpan<byte> bytes) => ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

    /// <summary>An address as its four numbers, as 192.168.1.20, the same in every regional format.</summary>
    internal static string AddressText(double value)
    {
        var address = (uint)Math.Clamp(value, 0, uint.MaxValue);
        return string.Create(CultureInfo.InvariantCulture, $"{address >> 24}.{(address >> 16) & 255}.{(address >> 8) & 255}.{address & 255}");
    }

    /// <summary>A time of day with its seconds, in the regional format's long time, as 12:34:56 PM.</summary>
    internal static string TimeText(double seconds, IFormatProvider culture) =>
        DateTime.MinValue.AddSeconds(Math.Clamp(Math.Floor(seconds), 0, SecondsPerDay - 1)).ToString("T", culture);

    // An id as the library makes them, its device, its type and its index, so its type is known from it alone.
    private static bool IsOwn(RawSensor sensor) => sensor.HardwareType == HardwareType;

    private static RawSensor Sensor(SensorKind kind, string type, string name, double? value) =>
        new($"{Device}/{type}/0", HardwareType, Hardware, kind.ToString(), name, value);
}
