using System.Net;
using Thermi.Core.Sensors;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// The live system's answers: its files from <c>/</c>, its clocks from <paramref name="time"/>, the default route's
/// address, and NVML when NVIDIA's driver is installed, started when a read asks it and shut when the readers let go of
/// it or the kernel closes.
/// </summary>
internal sealed class LiveKernel(TimeProvider time) : IKernel, IDisposable
{
    // A link of /sys leads to another and on, as a hwmon chip's device does, a few deep; far past that is a loop.
    private const int MaxLinks = 40;

    private NvmlLibrary? _nvml;

    // NVIDIA's driver was not there to start, which a service's life does not change.
    private bool _noNvml;

    public string? Read(string path)
    {
        try
        {
            return IKernel.Holds(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> List(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // As realpath does: a relative link leads from the folder it really is in, which a link above it may have moved.
    public string? Resolve(string path)
    {
        var real = "/";
        var rest = new Stack<string>(path.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse());
        for (var links = 0; rest.TryPop(out var part);)
        {
            var next = part == ".." ? Path.GetDirectoryName(real) ?? "/" : part == "." ? real : Path.Combine(real, part);
            var target = Target(next);
            if (target is null)
            {
                real = next;
                continue;
            }

            if (++links > MaxLinks)
            {
                return null;
            }

            foreach (var step in target.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                rest.Push(step);
            }

            real = target.StartsWith('/') ? "/" : real;
        }

        return Path.Exists(real) ? real : null;
    }

    public Moment Now() =>
        new((double)time.GetTimestamp() / time.TimestampFrequency, time.GetLocalNow(), Address());

    Moment? IKernel.Now() => Now();

    public IReadOnlyList<GpuAnswer>? Nvml()
    {
        if (_nvml is null && !_noNvml)
        {
            _nvml = NvmlLibrary.Open();
            _noNvml = _nvml is null;
        }

        return _nvml?.Read();
    }

    public void LetGoOfNvml()
    {
        _nvml?.Dispose();
        _nvml = null;
    }

    public void Next()
    {
        // The live system is always at its own second.
    }

    public TimeSpan Spent() => Environment.CpuUsage.TotalTime;

    public void Dispose() => LetGoOfNvml();

    private static string? Target(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // As a recording keeps it, its four numbers, the first highest.
    private static string? Address() =>
        SystemSensors.DefaultRouteAddress() is { } address
            ? new IPAddress([(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]).ToString()
            : null;
}
