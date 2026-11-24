using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Sensors;

namespace Thermi.Tests.Acceptance;

/// <summary>An item's Sensor dropdown lists its own device's sensors of its type, and the other devices' once asked to show all.</summary>
public sealed class SensorOfferTests
{
    private const string Cpu = "/amdcpu/0";
    private const string Nvidia = "/gpu-nvidia/0";

    // On the recorded PC, an item of a type that reads a sensor now, and one the item names, which it lists whatever the
    // dropdown leaves out: the devices it lists first and how many sensors, then the devices it lists once asked to show all.
    [Theory]
    [InlineData("Load", Nvidia + "/load/0", null, "/gpu-nvidia/0 x7", "/amdcpu/0 /gpu-amd/0 /nic /nvme/0 /ram /vram")]
    [InlineData("Load", Cpu + "/load/0", null, "/amdcpu/0 x34", "/gpu-amd/0 /gpu-nvidia/0 /nic /nvme/0 /ram /vram")]
    [InlineData("Load", Nvidia + "/load/7", Nvidia + "/load/7", "/gpu-nvidia/0 x8", "/amdcpu/0 /gpu-amd/0 /nic /nvme/0 /ram /vram")]
    [InlineData("Data", "/ram/data/0", null, "/ram x2", "/vram")]
    [InlineData("Throughput", MadeUpHardware.Ethernet + "/throughput/7", null, "/nic x2", "/gpu-nvidia/0 /nic /nvme/0")]
    [InlineData("Temperature", "/gone/0/temperature/0", null, Cpu + " /gpu-amd/0 " + Nvidia + " /lpc/it8689e/0 /memory/dimm/1 /memory/dimm/3 /nvme/0 x19", "")]
    public void TheSensorDropdownListsTheItemsOwnDeviceFirst(string type, string read, string? own, string first, string more)
    {
        var offer = SensorOffer.Of(SensorCatalog.Build(TestRecordings.Read(TestRecordings.NetDisk)), type, read, own);

        Assert.Equal(first, $"{Devices(offer.First)} x{offer.First.Count.ToString(CultureInfo.InvariantCulture)}");
        Assert.Equal(more, Devices(offer.More));
        Assert.Empty(offer.First.Intersect(offer.More));
    }

    // A network adapter's id is long and its own; the device is what lies before the sensor's type and index.
    private static string Devices(IEnumerable<string> ids) =>
        string.Join(' ', ids
            .Select(id => id[..id.LastIndexOf('/', id.LastIndexOf('/') - 1)])
            .Select(device => device.StartsWith("/nic/", StringComparison.Ordinal) ? "/nic" : device)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));
}
