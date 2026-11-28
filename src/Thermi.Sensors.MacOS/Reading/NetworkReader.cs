using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Sensors.Rates;
using Thermi.Sensors.MacOS.Source;

namespace Thermi.Sensors.MacOS.Reading;

/// <summary>
/// The network adapters, each second, with the rates they sent and received at and what they moved in all. An adapter is
/// physical when it is Ethernet, Wi-Fi among it, that IOKit's registry holds as a network interface of a device; the rest,
/// loopback, tunnels, VPNs' <c>utun</c>, bridges and Apple's peer-to-peer <c>awdl</c> and <c>llw</c>, are set apart as
/// virtual, as Windows' are, by what the Mac says of them, never by their names. The registry is asked only of an adapter
/// not seen before.
/// </summary>
internal sealed class NetworkReader
{
    public const string Interface = "IONetworkInterface";

    public static readonly string[] Keys = ["BSD Name", "IOBuiltin"];

    private const string HardwareType = "Network";
    private const int Ethernet = 6;
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    private readonly Dictionary<string, bool> _virtual = new(StringComparer.Ordinal);
    private readonly Counters _counters = new();

    /// <summary>The device ids of the adapters that are not physical, as the last read found them.</summary>
    public IReadOnlyList<string> Virtual { get; private set; } = [];

    public IEnumerable<RawSensor> Read(IMac mac, double? at)
    {
        var adapters = mac.Adapters();
        if (adapters.Any(adapter => !_virtual.ContainsKey(adapter.Name)))
        {
            var devices = mac.Registry(Interface, Keys)
                .Where(entry => entry.ContainsKey("IOBuiltin"))
                .Select(entry => MacJson.Text(entry["BSD Name"]))
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            foreach (var adapter in adapters.Where(adapter => !_virtual.ContainsKey(adapter.Name)))
            {
                _virtual[adapter.Name] = adapter.Loopback || adapter.Type != Ethernet || !devices.Contains(adapter.Name);
            }
        }

        var sensors = new List<RawSensor>();
        foreach (var adapter in adapters)
        {
            RawSensor Sensor(string type, int index, string name, double? value)
            {
                var id = string.Create(CultureInfo.InvariantCulture, $"{IdOf(adapter.Name)}/{type.ToLowerInvariant()}/{index}");
                return new(id, HardwareType, adapter.Name, type, name, value);
            }

            sensors.Add(Sensor("Throughput", 0, "Upload Speed", _counters.Rate($"{adapter.Name}/tx", adapter.Sent, at)));
            sensors.Add(Sensor("Throughput", 1, "Download Speed", _counters.Rate($"{adapter.Name}/rx", adapter.Received, at)));
            sensors.Add(Sensor("Data", 0, "Data Uploaded", adapter.Sent / BytesPerGigabyte));
            sensors.Add(Sensor("Data", 1, "Data Downloaded", adapter.Received / BytesPerGigabyte));
        }

        foreach (var gone in _virtual.Keys.Except(adapters.Select(adapter => adapter.Name), StringComparer.Ordinal).ToList())
        {
            _virtual.Remove(gone);
            _counters.Forget($"{gone}/tx");
            _counters.Forget($"{gone}/rx");
        }

        Virtual = [.. adapters.Where(adapter => _virtual[adapter.Name]).Select(adapter => IdOf(adapter.Name))];
        return sensors;
    }

    private static string IdOf(string adapter) => $"/nic/{adapter}";
}
