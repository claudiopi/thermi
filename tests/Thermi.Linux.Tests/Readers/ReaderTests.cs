using System.Globalization;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Sensors.Linux.Kernel;
using Thermi.Sensors.Linux.Reading;

namespace Thermi.Linux.Tests.Readers;

/// <summary>
/// The readers over trees of hardware the recorded PCs lack, made as the kernel's documentation lays out its files: a
/// laptop's battery beside a Bluetooth mouse's, AMD's and Intel's GPUs, RAPL's counter as it wraps, a chip with an
/// input numbered past any real one, a USB pump plugged again, which takes the CPU's chip's number, and a laptop's
/// NVIDIA card asleep. Each reads as Windows' reads the same hardware, and the sleeping card is asked nothing.
/// </summary>
public sealed class ReaderTests
{
    public enum Made
    {
        Laptop,
        AmdGpu,
        IntelGpu,
        RaplWrap,
        AbsurdInput,
        Replugged,
        SleepingNvidia,
    }

    [Theory] // Rows: each a reader, or a rule of one, that the recorded trees never reach.
    [InlineData(Made.Laptop)]
    [InlineData(Made.AmdGpu)]
    [InlineData(Made.IntelGpu)]
    [InlineData(Made.RaplWrap)]
    [InlineData(Made.AbsurdInput)]
    [InlineData(Made.Replugged)]
    [InlineData(Made.SleepingNvidia)]
    public void AMadeTreeReadsAsItsHardwareWould(Made made)
    {
        var root = Directory.CreateTempSubdirectory("thermi-made-").FullName;
        try
        {
            var (metrics, expected) = Build(new Tree(root), made);
            var sensors = new LinuxSensors(new RecordedKernel(root));
            sensors.Read();
            var second = sensors.Read();
            var readings = SensorMap.Read(second, OverlayConfig.First);

            Assert.Equal(expected, metrics.Select(metric => readings[metric.Id].Usable));
            // A mouse's battery is the mouse's, and names it by its address.
            Assert.DoesNotContain(second.Sensors, sensor => sensor.Id.Contains("hid-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // What each row's tree holds, and the metrics it must read, each second the same but RAPL's counter.
    private static (Metric[] Metrics, double?[] Expected) Build(Tree tree, Made made)
    {
        foreach (var second in (int[])[0, 1])
        {
            tree.Cpu(second);
            switch (made)
            {
                case Made.Laptop:
                    tree.Files(second, "/sys/class/power_supply/BAT0", ("type", "Battery"), ("status", "Discharging"), ("capacity", "80"),
                        ("power_now", "-10000000"), ("energy_now", "40000000"), ("voltage_now", "12000000"));
                    tree.Files(second, "/sys/class/power_supply/hid-a8:a1:59:12:34:56-battery", ("type", "Battery"), ("scope", "Device"),
                        ("status", "Discharging"), ("capacity", "50"));
                    break;
                case Made.AmdGpu:
                    tree.Gpu(second, "amdgpu", ("temp1_label", "edge"), ("temp1_input", "55000"), ("power1_label", "PPT"),
                        ("power1_average", "120000000"));
                    tree.Files(second, "/sys/class/drm/card1/device", ("gpu_busy_percent", "40"));
                    break;
                case Made.IntelGpu:
                    tree.Gpu(second, "xe", ("temp2_label", "pkg"), ("temp2_input", "48000"));
                    break;
                case Made.RaplWrap:
                    // Four joules a second across the counter's wrap: one before it, three after.
                    tree.Files(second, "/sys/class/powercap/intel-rapl:0", ("name", "package-0"), ("max_energy_range_uj", "262143328850"),
                        ("energy_uj", second == 0 ? "262142328850" : "3000000"));
                    break;
                case Made.AbsurdInput:
                    tree.Files(second, "/sys/class/hwmon/hwmon0", ("name", "k10temp"), ("temp1_label", "Tctl"), ("temp1_input", "60000"),
                        ("temp99999999999_input", "1000"));
                    tree.Link(second, "/sys/class/hwmon/hwmon0/device", "/sys/devices/pci0000:00/0000:00:18.3");
                    break;
                case Made.Replugged:
                    tree.Replugged(second);
                    break;
                case Made.SleepingNvidia:
                    tree.Files(second, "/sys/bus/pci/drivers/nvidia/0000:01:00.0/power", ("runtime_status", "suspended"));
                    tree.Files(second, "/sys/bus/pci/drivers/nvidia", ("new_id", ""));
                    tree.Nvml(second, new GpuAnswer("Made GPU") { Temperature = 50 });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(made), made, "Unknown tree.");
            }

            tree.Flush(second);
        }

        return made switch
        {
            Made.Laptop => ([Metrics.BatteryCharge, Metrics.BatteryRate, Metrics.BatteryTime], [80, -10, 4 * 3600]),
            Made.AmdGpu => ([Metrics.GpuTemperature, Metrics.GpuLoad, Metrics.GpuPower], [55, 40, 120]),
            Made.IntelGpu => ([Metrics.GpuTemperature], [48]),
            Made.RaplWrap => ([Metrics.CpuPower], [4]),
            Made.AbsurdInput or Made.Replugged => ([Metrics.CpuTemperature], [60]),
            Made.SleepingNvidia => ([Metrics.GpuTemperature], [null]),
            _ => throw new ArgumentOutOfRangeException(nameof(made), made, "Unknown tree."),
        };
    }

    // A tree as thermi-sensors --tree records one: each second's files at their escaped paths, its links and its moment.
    private sealed class Tree(string root)
    {
        private const string Card = "/sys/devices/pci0000:00/0000:00:03.1/0000:0b:00.0";

        private readonly Dictionary<string, string> _links = new(StringComparer.Ordinal);

        public void Files(int second, string folder, params (string Name, string Text)[] files)
        {
            foreach (var (name, text) in files)
            {
                var file = Path.Combine(root, $"{second}", RecordedKernel.Escaped($"{folder}/{name}"));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, text + "\n");
            }
        }

        public void Link(int second, string path, string target) => _links[$"{second}:{path}"] = target;

        // A CPU of two threads, which every PC has, and the memory.
        public void Cpu(int second)
        {
            Files(second, "/proc", ("cpuinfo", "model name\t: Made CPU\n"), ("meminfo", "MemTotal: 16000000 kB\nMemAvailable: 8000000 kB"),
                ("stat", string.Create(CultureInfo.InvariantCulture, $"cpu  {100 * second} 0 0 1000 0 0 0 0 0 0\ncpu0 0 0 0 0 0 0 0 0 0 0")));
        }

        // A GPU's card under /sys/class/drm, its driver, and the hwmon chip of its device.
        public void Gpu(int second, string driver, params (string Name, string Text)[] inputs)
        {
            Files(second, "/sys/class/hwmon/hwmon1", [("name", driver), .. inputs]);
            Files(second, "/sys/class/drm/card1/device", ("product_name", "Made GPU"));
            Link(second, "/sys/class/hwmon/hwmon1/device", Card);
            Link(second, "/sys/class/drm/card1/device", Card);
            Link(second, "/sys/class/drm/card1/device/driver", $"/sys/bus/pci/drivers/{driver}");
        }

        // The CPU's chip and a USB pump's, which is plugged again after the first second and takes hwmon0, the CPU's
        // chip's, whose chip moves to hwmon1: the device the kernel makes for the pump is a new one.
        public void Replugged(int second)
        {
            string[] numbers = second == 0 ? ["hwmon0", "hwmon1"] : ["hwmon1", "hwmon0"];
            Files(second, $"/sys/class/hwmon/{numbers[0]}", ("name", "k10temp"), ("temp1_label", "Tctl"), ("temp1_input", "60000"));
            Link(second, $"/sys/class/hwmon/{numbers[0]}/device", "/sys/devices/pci0000:00/0000:00:18.3");
            Files(second, $"/sys/class/hwmon/{numbers[1]}", ("name", "nzxt-kraken3"), ("temp1_input", "30000"));
            Link(second, $"/sys/class/hwmon/{numbers[1]}/device", $"/sys/devices/pci0000:00/0000:00:14.0/usb1/1-2/1-2:1.0/0003:1E71:2007.000{second + 1}");
        }

        public void Nvml(int second, GpuAnswer gpu) =>
            File.WriteAllText(Path.Combine(root, $"{second}", KernelJson.NvmlFile), KernelJson.Write([gpu]));

        public void Flush(int second)
        {
            var prefix = $"{second}:";
            var links = _links.Where(link => link.Key.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(link => link.Key[prefix.Length..], link => link.Value, StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(root, $"{second}", KernelJson.LinksFile), KernelJson.Write(links));
            var moment = new Moment(second, new DateTimeOffset(2026, 11, 27, 12, 0, second, TimeSpan.Zero), "192.0.2.7");
            File.WriteAllText(Path.Combine(root, $"{second}", KernelJson.MomentFile), KernelJson.Write(moment));
        }
    }
}
