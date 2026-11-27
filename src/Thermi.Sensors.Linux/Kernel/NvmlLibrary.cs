using System.Runtime.InteropServices;
using System.Text;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// NVIDIA's driver library, NVML, which answers any user: each GPU's temperature, loads, power, fans, memory and clocks.
/// It has no introspection data for GirCore, so its few calls are declared here, and it is opened only where the driver
/// installed it; a call that answers an error is a missing value.
/// </summary>
internal sealed partial class NvmlLibrary : IDisposable
{
    private const string Library = "libnvidia-ml.so.1";

    private const int Success = 0;

    // NVML_TEMPERATURE_GPU, and the clocks NVML_CLOCK_GRAPHICS and NVML_CLOCK_MEM.
    private const int GpuSensor = 0;
    private const int GraphicsClock = 0;
    private const int MemoryClockType = 2;

    // NVML_DEVICE_NAME_V2_BUFFER_SIZE.
    private const int NameLength = 96;

    // nvmlFanSpeedInfo_v1, as NVML_STRUCT_VERSION makes it: the struct's size, with its version in the top byte.
    private const uint FanSpeedVersion = (3 * sizeof(uint)) | (1u << 24);

    private const double BytesPerGigabyte = 1024d * 1024 * 1024;

    // A driver older than the calls that count a card's fans or read each one's duty or RPM lacks them; asked, they would
    // throw each second.
    private readonly bool _counts;
    private readonly bool _duties;
    private readonly bool _speeds;

    private bool _shut;

    private NvmlLibrary(bool counts, bool duties, bool speeds)
    {
        _counts = counts;
        _duties = duties;
        _speeds = speeds;
    }

    /// <summary>NVML started, or null where NVIDIA's driver is not installed or does not start.</summary>
    public static NvmlLibrary? Open()
    {
        if (!NativeLibrary.TryLoad(Library, typeof(NvmlLibrary).Assembly, null, out var handle))
        {
            return null;
        }

        var counts = NativeLibrary.TryGetExport(handle, "nvmlDeviceGetNumFans", out _);
        var duties = NativeLibrary.TryGetExport(handle, "nvmlDeviceGetFanSpeed_v2", out _);
        var speeds = NativeLibrary.TryGetExport(handle, "nvmlDeviceGetFanSpeedRPM", out _);
        NativeLibrary.Free(handle);
        try
        {
            return Init() == Success ? new NvmlLibrary(counts, duties, speeds) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Each GPU's answers, or null once NVML was shut.</summary>
    public IReadOnlyList<GpuAnswer>? Read()
    {
        if (_shut)
        {
            return null;
        }

        if (DeviceCount(out var count) != Success)
        {
            return [];
        }

        var gpus = new List<GpuAnswer>();
        for (var index = 0u; index < count; index++)
        {
            if (DeviceByIndex(index, out var device) == Success)
            {
                gpus.Add(Read(device));
            }
        }

        return gpus;
    }

    public void Dispose()
    {
        if (!_shut)
        {
            _shut = true;
            _ = Shutdown();
        }
    }

    private GpuAnswer Read(nint device)
    {
        var name = new byte[NameLength];
        var named = Name(device, name, NameLength) == Success;
        var used = UtilizationRates(device, out var load) == Success;
        var memory = MemoryOf(device, out var bytes) == Success;
        var end = Array.IndexOf(name, (byte)0);
        return WithFans(device, new GpuAnswer(named ? Encoding.UTF8.GetString(name, 0, end < 0 ? NameLength : end) : "NVIDIA GPU")
        {
            Temperature = Temperature(device, GpuSensor, out var degrees) == Success ? degrees : null,
            Load = used ? load.Gpu : null,
            MemoryLoad = used ? load.Memory : null,
            Power = PowerUsage(device, out var milliwatts) == Success ? milliwatts / 1000d : null,
            Fan = FanSpeed(device, out var fan) == Success ? fan : null,
            MemoryUsed = memory ? bytes.Used / BytesPerGigabyte : null,
            MemoryTotal = memory ? bytes.Total / BytesPerGigabyte : null,
            CoreClock = ClockOf(device, GraphicsClock, out var core) == Success ? core : null,
            MemoryClock = ClockOf(device, MemoryClockType, out var clock) == Success ? clock : null,
        });
    }

    private GpuAnswer WithFans(nint device, GpuAnswer gpu)
    {
        var fans = _counts && NumFans(device, out var count) == Success ? count : (uint?)null;
        return gpu with
        {
            FanDuties = _duties ? EachFan(fans, fan => Duty(device, fan)) : null,
            FanSpeeds = EachFan(fans, fan => _speeds ? Rpm(device, fan) : null),
        };
    }

    // What each of a card's fans reads, a stopped fan's 0 as NVML answers it, or null where the driver cannot count them.
    private static double?[]? EachFan(uint? count, Func<uint, double?> read) =>
        count is { } fans ? [.. Enumerable.Range(0, (int)fans).Select(fan => read((uint)fan))] : null;

    private static double? Duty(nint device, uint fan) => FanSpeedOf(device, fan, out var percent) == Success ? percent : null;

    private static double? Rpm(nint device, uint fan)
    {
        var info = new FanSpeedInfo { Version = FanSpeedVersion, Fan = fan };
        return FanSpeedRpm(device, ref info) == Success ? info.Speed : null;
    }

    [LibraryImport(Library, EntryPoint = "nvmlInit_v2")]
    private static partial int Init();

    [LibraryImport(Library, EntryPoint = "nvmlShutdown")]
    private static partial int Shutdown();

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetCount_v2")]
    private static partial int DeviceCount(out uint count);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static partial int DeviceByIndex(uint index, out nint device);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetName")]
    private static partial int Name(nint device, [Out] byte[] name, uint length);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetTemperature")]
    private static partial int Temperature(nint device, int sensor, out uint degrees);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static partial int UtilizationRates(nint device, out Utilization rates);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetPowerUsage")]
    private static partial int PowerUsage(nint device, out uint milliwatts);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetFanSpeed")]
    private static partial int FanSpeed(nint device, out uint percent);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetFanSpeed_v2")]
    private static partial int FanSpeedOf(nint device, uint fan, out uint percent);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetNumFans")]
    private static partial int NumFans(nint device, out uint count);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetFanSpeedRPM")]
    private static partial int FanSpeedRpm(nint device, ref FanSpeedInfo speed);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetMemoryInfo")]
    private static partial int MemoryOf(nint device, out MemoryInfo memory);

    [LibraryImport(Library, EntryPoint = "nvmlDeviceGetClockInfo")]
    private static partial int ClockOf(nint device, int type, out uint megahertz);

    // nvmlUtilization_t: percents of the last sample period the GPU and its memory were busy.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Utilization
    {
        public uint Gpu { get; init; }
        public uint Memory { get; init; }
    }

    // nvmlFanSpeedInfo_t: the struct's version and the fan's index in, its speed in RPM out.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FanSpeedInfo
    {
        public uint Version { get; init; }
        public uint Fan { get; init; }
        public uint Speed { get; init; }
    }

    // nvmlMemory_t, in bytes.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MemoryInfo
    {
        public ulong Total { get; init; }
        public ulong Free { get; init; }
        public ulong Used { get; init; }
    }
}
