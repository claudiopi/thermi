using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// The GPUs: NVIDIA's through NVML, as LibreHardwareMonitor names their sensors on Windows, and AMD's and Intel's from
/// their <c>hwmon</c> chip, whose names the kernel gives (<c>edge</c>, <c>junction</c>, <c>PPT</c>), and their card under
/// <c>/sys/class/drm</c>, which tells how busy each is and its memory. NVIDIA's own driver leaves its cards out of both.
/// </summary>
internal sealed class GpuReader(IKernel kernel, IReadOnlyList<Chip> chips)
{
    private const string Drm = "/sys/class/drm";
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    // A card's own folder, card0, and not its outputs, as card0-HDMI-A-1.
    private readonly IReadOnlyList<Card> _cards =
        [.. kernel.List(Drm).Where(card => card.StartsWith("card", StringComparison.Ordinal) && card[4..].All(char.IsAsciiDigit))
            .Select(card => CardOf(kernel, card, chips)).OfType<Card>()];

    public IEnumerable<RawSensor> Read()
    {
        var sensors = new List<RawSensor>();
        foreach (var (gpu, index) in (kernel.Nvml() ?? []).Select((gpu, index) => (gpu, index)))
        {
            sensors.AddRange(Nvidia(gpu, string.Create(CultureInfo.InvariantCulture, $"/gpu-nvidia/{index}")));
        }

        foreach (var card in _cards)
        {
            var id = $"/{card.HardwareType.Replace("Gpu", "gpu-", StringComparison.Ordinal).ToLowerInvariant()}/{card.Device}";
            foreach (var chip in card.Chips)
            {
                sensors.AddRange(Hwmon.Read(kernel, chip, id, card.HardwareType, card.Name));
            }

            double? Value(string file)
            {
                return Counters.Number(kernel.Read($"{Drm}/{card.Folder}/device/{file}"));
            }

            sensors.Add(new($"{id}/load/0", card.HardwareType, card.Name, "Load", "GPU Core", Value("gpu_busy_percent")));
            sensors.Add(new($"{id}/data/0", card.HardwareType, card.Name, "Data", "GPU Memory Used", Value("mem_info_vram_used") / BytesPerGigabyte));
            sensors.Add(new($"{id}/data/1", card.HardwareType, card.Name, "Data", "GPU Memory Total", Value("mem_info_vram_total") / BytesPerGigabyte));
        }

        return sensors;
    }

    private static IEnumerable<RawSensor> Nvidia(GpuAnswer gpu, string id)
    {
        const string HardwareType = "GpuNvidia";
        RawSensor Sensor(string type, int index, string name, double? value)
        {
            return new(string.Create(CultureInfo.InvariantCulture, $"{id}/{type.ToLowerInvariant()}/{index}"), HardwareType, gpu.Name, type, name, value);
        }

        return
        [
            Sensor("Temperature", 0, "GPU Core", gpu.Temperature),
            Sensor("Load", 0, "GPU Core", gpu.Load),
            Sensor("Load", 1, "GPU Memory Controller", gpu.MemoryLoad),
            Sensor("Power", 0, "GPU Package", gpu.Power),
            Sensor("Control", 0, "GPU Fan", gpu.Fan),
            Sensor("Data", 0, "GPU Memory Used", gpu.MemoryUsed),
            Sensor("Data", 1, "GPU Memory Total", gpu.MemoryTotal),
            Sensor("Clock", 0, "GPU Core", gpu.CoreClock),
            Sensor("Clock", 1, "GPU Memory", gpu.MemoryClock),
        ];
    }

    // A card of AMD's or Intel's drivers, with the hwmon chips of its device.
    private static Card? CardOf(IKernel kernel, string card, IReadOnlyList<Chip> chips)
    {
        var driver = kernel.Resolve($"{Drm}/{card}/device/driver") is { } path ? Path.GetFileName(path) : null;
        var hardwareType = driver switch
        {
            "amdgpu" or "radeon" => "GpuAmd",
            "i915" or "xe" => "GpuIntel",
            _ => null,
        };
        if (hardwareType is null || kernel.Resolve($"{Drm}/{card}/device") is not { } resolved)
        {
            return null;
        }

        var device = Hwmon.DevicePath(resolved);
        var named = kernel.Read($"{Drm}/{card}/device/product_name")?.Trim();
        var name = named is { Length: > 0 } ? named : hardwareType == "GpuAmd" ? "AMD Radeon GPU" : "Intel Graphics";
        return new Card(card, device, hardwareType, name, [.. chips.Where(chip => chip.Device == device)]);
    }

    private sealed record Card(string Folder, string Device, string HardwareType, string Name, IReadOnlyList<Chip> Chips);
}
