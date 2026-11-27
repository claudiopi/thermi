using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Sensors.Linux.Kernel;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// The GPUs: NVIDIA's through NVML, as LibreHardwareMonitor names their sensors on Windows, and AMD's and Intel's from
/// their <c>hwmon</c> chip, whose names the kernel gives (<c>edge</c>, <c>junction</c>, <c>PPT</c>), and their card under
/// <c>/sys/class/drm</c>, which tells how busy each is and its memory. NVIDIA's own driver leaves its cards out of both.
/// A card that sleeps, as a laptop's does while its graphics chip draws alone, is left asleep: NVML is shut while every
/// card of NVIDIA's driver sleeps, as its open handles keep a card awake and a query wakes one, and a card of AMD's or
/// Intel's reads as missing, as amdgpu's reads wake it on kernels before 6.13.
/// </summary>
internal sealed class GpuReader(IKernel kernel, IReadOnlyList<Chip> chips)
{
    private const string Drm = "/sys/class/drm";
    private const string NvidiaCards = "/sys/bus/pci/drivers/nvidia";
    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    // A card's own folder, card0, and not its outputs, as card0-HDMI-A-1.
    private readonly IReadOnlyList<Card> _cards =
        [.. kernel.List(Drm).Where(card => card.StartsWith("card", StringComparison.Ordinal) && card[4..].All(char.IsAsciiDigit))
            .Select(card => CardOf(kernel, card, chips)).OfType<Card>()];

    public IEnumerable<RawSensor> Read()
    {
        var sensors = new List<RawSensor>();
        if (NvidiaSleeps())
        {
            kernel.LetGoOfNvml();
        }
        else
        {
            foreach (var (gpu, index) in (kernel.Nvml() ?? []).Select((gpu, index) => (gpu, index)))
            {
                sensors.AddRange(Nvidia(gpu, string.Create(CultureInfo.InvariantCulture, $"/gpu-nvidia/{index}")));
            }
        }

        foreach (var card in _cards)
        {
            var id = $"/{card.HardwareType.Replace("Gpu", "gpu-", StringComparison.Ordinal).ToLowerInvariant()}/{card.Device}";
            var asleep = Asleep($"{Drm}/{card.Folder}/device");
            foreach (var chip in card.Chips)
            {
                sensors.AddRange(Hwmon.Read(kernel, chip, id, card.HardwareType, card.Name, asleep));
            }

            double? Value(string file)
            {
                return asleep ? null : Counters.Number(kernel.Read($"{Drm}/{card.Folder}/device/{file}"));
            }

            sensors.Add(new($"{id}/load/0", card.HardwareType, card.Name, "Load", "GPU Core", Value("gpu_busy_percent")));
            sensors.Add(new($"{id}/data/0", card.HardwareType, card.Name, "Data", "GPU Memory Used", Value("mem_info_vram_used") / BytesPerGigabyte));
            sensors.Add(new($"{id}/data/1", card.HardwareType, card.Name, "Data", "GPU Memory Total", Value("mem_info_vram_total") / BytesPerGigabyte));
        }

        return sensors;
    }

    // The driver's folder lists its cards by their PCI address, as 0000:01:00.0, beside its own files.
    private bool NvidiaSleeps()
    {
        var cards = kernel.List(NvidiaCards)
            .Where(entry => entry.Length > 0 && char.IsAsciiHexDigit(entry[0]) && entry.Contains(':', StringComparison.Ordinal))
            .ToList();
        return cards.Count > 0 && cards.All(card => Asleep($"{NvidiaCards}/{card}"));
    }

    private bool Asleep(string device) => kernel.Read($"{device}/power/runtime_status")?.Trim() == "suspended";

    // LibreHardwareMonitor numbers a card's fans and their duties from 1, as NVIDIA's own API numbers its coolers, and
    // names each by its number only past one. Where NVML cannot give each fan's duty, the card's one duty stands for
    // them, and where it cannot count the fans, the card has one, which reads missing.
    private static IEnumerable<RawSensor> Nvidia(GpuAnswer gpu, string id)
    {
        const string HardwareType = "GpuNvidia";
        RawSensor Sensor(string type, int index, string name, double? value)
        {
            return new(string.Create(CultureInfo.InvariantCulture, $"{id}/{type.ToLowerInvariant()}/{index}"), HardwareType, gpu.Name, type, name, value);
        }

        IEnumerable<RawSensor> Fans(string type, IReadOnlyList<double?> values)
        {
            return values.Select((value, fan) =>
                Sensor(type, fan + 1, values.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"GPU Fan {fan + 1}") : "GPU Fan", value));
        }

        return
        [
            Sensor("Temperature", 0, "GPU Core", gpu.Temperature),
            Sensor("Load", 0, "GPU Core", gpu.Load),
            Sensor("Load", 1, "GPU Memory Controller", gpu.MemoryLoad),
            Sensor("Power", 0, "GPU Package", gpu.Power),
            .. Fans("Control", gpu.FanDuties ?? [gpu.Fan]),
            .. Fans("Fan", gpu.FanSpeeds ?? [null]),
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
