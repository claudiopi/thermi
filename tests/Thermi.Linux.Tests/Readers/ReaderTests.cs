using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Sensors.Linux.Kernel;
using Thermi.Sensors.Linux.Reading;

namespace Thermi.Linux.Tests.Readers;

/// <summary>
/// The readers over trees of hardware the recorded PCs lack, made as the kernel's documentation lays out its files: a
/// laptop's battery beside a Bluetooth mouse's, AMD's and Intel's GPUs, RAPL's counter as it wraps, a chip with an
/// input numbered past any real one, a USB pump plugged again, which takes the CPU's chip's number, and a laptop's
/// NVIDIA and AMD cards falling asleep. Each reads as Windows' reads the same hardware; a sleeping card is read as
/// missing, and NVML, whose open handles keep a card awake, is held only while the card is.
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
        SleepingAmdGpu,
        NvidiaFans,
        NvidiaUncounted,
    }

    [Theory] // Rows: each a reader, or a rule of one, that the recorded trees never reach.
    [InlineData(Made.Laptop)]
    [InlineData(Made.AmdGpu)]
    [InlineData(Made.IntelGpu)]
    [InlineData(Made.RaplWrap)]
    [InlineData(Made.AbsurdInput)]
    [InlineData(Made.Replugged)]
    [InlineData(Made.SleepingNvidia)]
    [InlineData(Made.SleepingAmdGpu)]
    [InlineData(Made.NvidiaFans)]
    [InlineData(Made.NvidiaUncounted)]
    public void AMadeTreeReadsAsItsHardwareWould(Made made)
    {
        var root = Directory.CreateTempSubdirectory("thermi-made-").FullName;
        try
        {
            var (metrics, expected) = Build(new Tree(root), made);
            var kernel = new NvmlHeld(new RecordedKernel(root));
            var sensors = new LinuxSensors(kernel);
            sensors.Read();
            var second = sensors.Read();
            var readings = SensorMap.Read(second, OverlayConfig.First);

            Assert.Equal(expected, metrics.Select(metric => readings[metric.Id].Usable));
            // An NVIDIA card's fans have no metric, so they are found by the id and name Windows' recordings give them.
            Assert.Equal(
                Fans(made),
                second.Sensors.Where(sensor => sensor.HardwareType == "GpuNvidia" && sensor.Type is "Fan" or "Control")
                    .Select(sensor => (sensor.Id, sensor.Name, sensor.Value)));
            var awake = made is Made.NvidiaFans or Made.NvidiaUncounted;
            Assert.Equal((awake || made == Made.SleepingNvidia ? 1 : 0, awake), (kernel.Starts, kernel.Held));
            // A mouse's battery is the mouse's, and names it by its address.
            Assert.DoesNotContain(second.Sensors, sensor => sensor.Id.Contains("hid-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // A folder of no second is no tree, a person's slip that is said in one line, never a stack trace.
    [Fact(Timeout = Bound.Test)]
    public async Task AFolderOfNoSecondEndsWithWhy()
    {
        var root = Directory.CreateTempSubdirectory("thermi-empty-").FullName;
        try
        {
            using var error = new StringWriter();
            string[] args = ["--root", root, "--dump", Path.Combine(root, "dump.jsonl")];

            var exit = await Thermi.Sensors.Linux.Program.RunAsync(args, new FakeTimeProvider(), TextWriter.Null, error, TestContext.Current.CancellationToken);

            Assert.Equal((2, 1), (exit, error.ToString().Trim().Split('\n').Length));
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
                case Made.AmdGpu or Made.SleepingAmdGpu:
                    tree.Asleep(second, "/sys/class/drm/card1/device", made == Made.SleepingAmdGpu && second == 1);
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
                    tree.Asleep(second, "/sys/bus/pci/drivers/nvidia/0000:01:00.0", second == 1);
                    tree.Files(second, "/sys/bus/pci/drivers/nvidia", ("new_id", ""));
                    tree.Nvml(second, new GpuAnswer("Made GPU") { Temperature = 50 });
                    break;
                case Made.NvidiaFans:
                    // Two fans, one stopped, as a card stops its fans while it is cool.
                    tree.Nvml(second, new GpuAnswer("Made GPU") { Temperature = 50, Fan = 30, FanDuties = [30, 0], FanSpeeds = [1500, 0] });
                    break;
                case Made.NvidiaUncounted:
                    // A driver older than the calls that count a card's fans and read each one's duty and speed.
                    tree.Nvml(second, new GpuAnswer("Made GPU") { Temperature = 50, Fan = 40 });
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
            Made.SleepingAmdGpu => ([Metrics.GpuTemperature, Metrics.GpuLoad, Metrics.GpuPower], [null, null, null]),
            Made.NvidiaFans or Made.NvidiaUncounted => ([Metrics.GpuTemperature], [50]),
            _ => throw new ArgumentOutOfRangeException(nameof(made), made, "Unknown tree."),
        };
    }

    // The NVIDIA card's duties and fans each row must read: a fan whose speed NVML cannot give reads missing, never 0.
    private static (string Id, string Name, double? Value)[] Fans(Made made) =>
        made == Made.NvidiaFans
            ?
            [
                ("/gpu-nvidia/0/control/1", "GPU Fan 1", 30), ("/gpu-nvidia/0/control/2", "GPU Fan 2", 0),
                ("/gpu-nvidia/0/fan/1", "GPU Fan 1", 1500), ("/gpu-nvidia/0/fan/2", "GPU Fan 2", 0),
            ]
        : made == Made.NvidiaUncounted ? [("/gpu-nvidia/0/control/1", "GPU Fan", 40), ("/gpu-nvidia/0/fan/1", "GPU Fan", null)]
        : [];

    // A kernel that holds NVML as the live one does: started by a read the driver answers, held until let go of.
    private sealed class NvmlHeld(IKernel kernel) : IKernel
    {
        public int Starts { get; private set; }

        public bool Held { get; private set; }

        public string? Read(string path) => kernel.Read(path);

        public IReadOnlyList<string> List(string folder) => kernel.List(folder);

        public string? Resolve(string path) => kernel.Resolve(path);

        public Moment? Now() => kernel.Now();

        public IReadOnlyList<GpuAnswer>? Nvml()
        {
            var gpus = kernel.Nvml();
            if (gpus is not null && !Held)
            {
                Starts++;
                Held = true;
            }

            return gpus;
        }

        public void LetGoOfNvml() => Held = false;

        public void Next() => kernel.Next();
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

        public void Asleep(int second, string device, bool asleep) => Files(second, $"{device}/power", ("runtime_status", asleep ? "suspended" : "active"));

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
