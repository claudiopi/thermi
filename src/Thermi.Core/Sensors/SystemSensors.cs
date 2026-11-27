using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Thermi.Core.Settings;

namespace Thermi.Core.Sensors;

/// <summary>
/// What the OS itself says, with no sensor: how long it has run, the IPv4 address its default route goes out from, if any,
/// and the local time now; null where it could not be told, as from a recording that holds none.
/// </summary>
public sealed record SystemFacts(TimeSpan? Uptime, uint? Address, DateTimeOffset? LocalNow);

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

    // An address of the range kept for documentation, beyond any local network.
    private static readonly IPEndPoint Beyond = new(IPAddress.Parse("192.0.2.1"), 9);

    /// <summary>
    /// The snapshot with the PC's sensors added, and while thermi's driver may be missing, each core's VID read as 0, so as
    /// missing: the library then reads the same VID for every core, 1.55 V on a Ryzen 9 9950X, which no core asks for at rest.
    /// </summary>
    public static Snapshot With(Snapshot snapshot, SystemFacts facts) => snapshot with
    {
        Sensors =
        [
            .. SensorMap.DriverMayBeMissing(snapshot) ? snapshot.Sensors.Select(Unread) : snapshot.Sensors,
            Sensor(SensorKind.Duration, "duration", Uptime, facts.Uptime is { } uptime ? Math.Floor(uptime.TotalSeconds) : null),
            Sensor(SensorKind.Address, "address", LocalIp, facts.Address),
            Sensor(SensorKind.Time, "time", Clock, facts.LocalNow is { } now ? Math.Floor(now.TimeOfDay.TotalSeconds) : null),
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

    /// <summary>
    /// The IPv4 address the default route goes out from, as one number, or null with no route out. Connecting a datagram
    /// socket to an address beyond the local network only picks the route, and sends nothing.
    /// </summary>
    public static uint? DefaultRouteAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(Beyond);
            return socket.LocalEndPoint is IPEndPoint { Address: var local } && !IPAddress.IsLoopback(local) ? AddressOf(local.GetAddressBytes()) : null;
        }
        catch (SocketException)
        {
            return null;
        }
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

    private static RawSensor Unread(RawSensor sensor) =>
        sensor is { HardwareType: "Cpu", Type: nameof(SensorKind.Voltage) } && sensor.Name.EndsWith(" VID", StringComparison.Ordinal)
            ? sensor with { Value = 0 }
            : sensor;

    private static RawSensor Sensor(SensorKind kind, string type, string name, double? value) =>
        new($"{Device}/{type}/0", HardwareType, Hardware, kind.ToString(), name, value);
}
