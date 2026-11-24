using Thermi.Core.Sensors;

namespace Thermi.Tests;

/// <summary>
/// What the author's PC cannot show, made up in the shape LibreHardwareMonitor 0.9.6 reports it (its hardware and sensor
/// types, names and ids): a second drive beside the recorded one, and a laptop's battery and virtual adapter. A real
/// recording of each replaces them.
/// </summary>
internal static class MadeUpHardware
{
    /// <summary>The recorded adapters and drive, from <see cref="TestRecordings.NetDisk"/>: the Ethernet one moving data, the Wi-Fi one idle.</summary>
    public const string Ethernet = "/nic/%7B12406381-1BFB-4568-8315-73797B3F896D%7D";
    public const string WiFi = "/nic/%7B71C89706-EB0D-4A76-8A35-8CE7E9353E5E%7D";
    public const string Samsung = "/nvme/0";

    public const string Wsl = "/nic/%7BC0A8E3F7-1B22-4D5C-8E90-6A7B3D1F2C04%7D";
    public const string Western = "/nvme/1";
    public const string Battery = "/battery/0";

    /// <summary>
    /// The recording made while the author downloaded and copied files, with a second drive made up beside its own, idle but
    /// for <paramref name="westernActivity"/>.
    /// </summary>
    public static Snapshot Desktop(double westernActivity = 1)
    {
        var recorded = TestRecordings.Read(TestRecordings.NetDisk);
        return recorded with { Sensors = [.. recorded.Sensors, .. Drive(Western, "WD_BLACK SN850X 1000GB", westernActivity, read: 12_400_000, write: 999)] };
    }

    /// <summary>
    /// A laptop's battery, discharging, and two adapters: Wi-Fi, which moves a little, and WSL's virtual switch, which has
    /// carried more, with <paramref name="wifiMoved"/> the data the Wi-Fi one has moved.
    /// </summary>
    public static Snapshot Laptop(double wifiMoved = 2.4) => new([
        new RawSensor($"{Battery}/level/0", "Battery", "Primary Battery", "Level", "Charge Level", 87.6),
        new RawSensor($"{Battery}/level/1", "Battery", "Primary Battery", "Level", "Degradation Level", 4.1),
        new RawSensor($"{Battery}/voltage/0", "Battery", "Primary Battery", "Voltage", "Voltage", 12.6),
        new RawSensor($"{Battery}/power/0", "Battery", "Primary Battery", "Power", "Discharge Rate", 12.4),
        new RawSensor($"{Battery}/energy/2", "Battery", "Primary Battery", "Energy", "Remaining Capacity", 48_200),
        new RawSensor($"{Battery}/timespan/0", "Battery", "Primary Battery", "TimeSpan", "Remaining Time (Estimated)", 8040),
        .. Adapter(WiFi, "Wi-Fi", downloaded: wifiMoved, uploaded: 0, download: 812_000, upload: 0),
        .. Adapter(Wsl, "vEthernet (WSL (Hyper-V firewall))", downloaded: 30, uploaded: 6, download: 50_000_000, upload: 0),
    ])
    {
        VirtualAdapters = [Wsl],
    };

    private static RawSensor[] Adapter(string id, string name, double downloaded, double uploaded, double download, double upload) =>
    [
        new($"{id}/load/1", "Network", name, "Load", "Network Utilization", 3.2),
        new($"{id}/data/2", "Network", name, "Data", "Data Uploaded", uploaded),
        new($"{id}/data/3", "Network", name, "Data", "Data Downloaded", downloaded),
        new($"{id}/throughput/7", "Network", name, "Throughput", "Upload Speed", upload),
        new($"{id}/throughput/8", "Network", name, "Throughput", "Download Speed", download),
    ];

    // A drive with the ids the recorded one has.
    private static RawSensor[] Drive(string id, string name, double activity, double read, double write) =>
    [
        new($"{id}/temperature/0", "Storage", name, "Temperature", "Composite Temperature", 41),
        new($"{id}/load/30", "Storage", name, "Load", "Used Space", 61.2),
        new($"{id}/load/51", "Storage", name, "Load", "Read Activity", activity / 2),
        new($"{id}/load/52", "Storage", name, "Load", "Write Activity", activity / 2),
        new($"{id}/load/53", "Storage", name, "Load", "Total Activity", activity),
        new($"{id}/throughput/54", "Storage", name, "Throughput", "Read Rate", read),
        new($"{id}/throughput/55", "Storage", name, "Throughput", "Write Rate", write),
    ];
}
