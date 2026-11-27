using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// Each network adapter of <c>/sys/class/net</c>, listed each second as adapters come and go: its speeds and what it moved
/// since the PC started. One with no device behind it, as loopback, a bridge, a container's or a VPN's, is virtual, as
/// Windows' are by its own flags; whether it is, is found once for each adapter, and one that is gone is forgotten, as a
/// container's that come and go by the hundred.
/// </summary>
internal sealed class NetworkReader
{
    private const string HardwareType = "Network";
    private const string Net = "/sys/class/net";
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    private readonly Dictionary<string, bool> _virtual = new(StringComparer.Ordinal);
    private readonly Counters _counters = new();

    /// <summary>The device ids of the adapters that are not physical, as the last read found them.</summary>
    public IReadOnlyList<string> Virtual { get; private set; } = [];

    public static string IdOf(string adapter) => $"/nic/{adapter}";

    public IEnumerable<RawSensor> Read(IKernel kernel, double? at)
    {
        var adapters = kernel.List(Net);
        var sensors = new List<RawSensor>();
        foreach (var adapter in adapters)
        {
            if (!_virtual.ContainsKey(adapter))
            {
                _virtual[adapter] = kernel.Resolve($"{Net}/{adapter}/device") is null;
            }

            var sent = Counters.Number(kernel.Read($"{Net}/{adapter}/statistics/tx_bytes"));
            var received = Counters.Number(kernel.Read($"{Net}/{adapter}/statistics/rx_bytes"));
            RawSensor Sensor(string type, int index, string name, double? value)
            {
                var id = string.Create(CultureInfo.InvariantCulture, $"{IdOf(adapter)}/{type.ToLowerInvariant()}/{index}");
                return new(id, HardwareType, adapter, type, name, value);
            }

            sensors.Add(Sensor("Throughput", 0, "Upload Speed", _counters.Rate($"{adapter}/tx", sent, at)));
            sensors.Add(Sensor("Throughput", 1, "Download Speed", _counters.Rate($"{adapter}/rx", received, at)));
            sensors.Add(Sensor("Data", 0, "Data Uploaded", sent / BytesPerGigabyte));
            sensors.Add(Sensor("Data", 1, "Data Downloaded", received / BytesPerGigabyte));
        }

        foreach (var gone in _virtual.Keys.Except(adapters, StringComparer.Ordinal).ToList())
        {
            _virtual.Remove(gone);
            _counters.Forget($"{gone}/tx");
            _counters.Forget($"{gone}/rx");
        }

        Virtual = [.. adapters.Where(adapter => _virtual[adapter]).Select(IdOf)];
        return sensors;
    }
}
