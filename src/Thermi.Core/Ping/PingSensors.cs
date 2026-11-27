using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Ping;

/// <summary>Why an echo brought no round trip back.</summary>
public enum PingFailure
{
    /// <summary>No reply came within the echo's timeout.</summary>
    TimedOut = 1,

    /// <summary>A router or the host itself said the host cannot be reached.</summary>
    Unreachable,

    /// <summary>The host's name resolves to no address.</summary>
    UnknownHost,

    /// <summary>The PC has no network up, so no echo was sent.</summary>
    NoNetwork,

    /// <summary>An item reads the ping, and no echo to its host has ended in the last two seconds, as before the first.</summary>
    Waiting,
}

/// <summary>What one echo brought back: its round trip in milliseconds, or why there is none.</summary>
public sealed record EchoReply(double? RoundTrip, PingFailure? Failure)
{
    public static EchoReply Of(double milliseconds) => new(milliseconds, null);

    public static EchoReply Failed(PingFailure failure) => new(null, failure);
}

/// <summary>
/// The sensor of a device of thermi's own under Network, the ping, that every reader adds to each snapshot as it adds the
/// PC's (<see cref="SystemSensors"/>): the round trip of its last echo to the host the settings name, read only while an
/// item of some overlay reads it (<c>docs/adr/0020-ping.md</c>). Its value carries why it has none as a negative number,
/// which no round trip is and no reading shows, so the catalog, the overlay's tooltip and an agent can say which.
/// </summary>
public static class PingSensors
{
    public const string HardwareType = "Ping";

    public const string Name = "Ping";

    /// <summary>Cloudflare's resolver, answering from near almost any PC, which the settings ping when they name no host.</summary>
#pragma warning disable S1313 // The host pinged when the person names none, which the window's hint and the docs name too.
    public const string DefaultHost = "1.1.1.1";
#pragma warning restore S1313

    /// <summary>The longest a host name may be, as DNS has it.</summary>
    public const int MaxHostLength = 253;

    public const string Id = "/ping/0/latency/0";

    /// <summary>The snapshot with the ping's sensor added: <paramref name="reply"/>'s round trip, or why there is none, or with no reply nothing yet.</summary>
    public static Snapshot With(Snapshot snapshot, EchoReply? reply)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var value = reply is not { } read ? null : read.Failure is { } failure ? -(int)failure : read.RoundTrip;
        return snapshot with { Sensors = [.. snapshot.Sensors, new RawSensor(Id, HardwareType, Name, nameof(SensorKind.Latency), Name, value)] };
    }

    public static bool Is(RawSensor sensor) => sensor.HardwareType == HardwareType;

    /// <summary>Whether the sensor is the ping's with a failure for its value, which reads as missing, never as a number.</summary>
    public static bool Failed(RawSensor sensor) => Is(sensor) && sensor.Value < 0;

    /// <summary>Why the ping's sensor reads nothing: the failure its value carries, or, with none, that no item reads it yet.</summary>
    public static NoValue WhyOf(RawSensor sensor) => (sensor.Value is { } value ? (PingFailure)(int)-value : (PingFailure?)null) switch
    {
        PingFailure.TimedOut => NoValue.PingTimedOut,
        PingFailure.Unreachable => NoValue.PingUnreachable,
        PingFailure.UnknownHost => NoValue.PingUnknownHost,
        PingFailure.NoNetwork => NoValue.PingNoNetwork,
        PingFailure.Waiting => NoValue.PingWaiting,
        _ => NoValue.PingOff,
    };

    /// <summary>
    /// The host to ping now: the settings' own, or <see cref="DefaultHost"/>, while an item of any overlay, shown or hidden,
    /// reads the ping, since thermi's mark and tooltip judge every item; else null, and nothing is sent.
    /// </summary>
    public static string? HostOf(ThermiConfig? config) =>
        config is not null && config.Overlays.Any(overlay => overlay.Items.Any(Reads)) ? config.PingHost ?? DefaultHost : null;

    /// <summary>Whether an item reads the ping: its metric, or its sensor, alone or among those it combines, or what it warns by.</summary>
    public static bool Reads(ItemConfig item) =>
        item.Metric == Metrics.Ping.Id || item.Sensor == Id || (item.Sensors?.Contains(Id, StringComparer.Ordinal) ?? false)
        || item.WarnBy?.Metric == Metrics.Ping.Id || item.WarnBy?.Sensor == Id;

    /// <summary>Whether <paramref name="host"/> is a host to ping: a DNS name or an IPv4 or IPv6 address, of up to 253 characters.</summary>
    public static bool IsHost(string host) =>
        host.Length is > 0 and <= MaxHostLength && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}
