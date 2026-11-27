using System.Globalization;
using System.Text.RegularExpressions;

namespace Thermi.Sensors.Linux.Kernel;

/// <summary>
/// The answers of <paramref name="kernel"/>, kept as <see cref="RecordedKernel"/> replays them: each file read, at its path
/// under the second's folder of <paramref name="tree"/>, and the second's answers that are no file. What the readers read
/// names no PC or person; of what it does, the address is moved into the range kept for documentation (<see cref="Moment.Scrubbed"/>),
/// and a line of <c>/proc/cpuinfo</c> with a board's serial dropped, both before the readers read them, so a replay of the
/// recording reads as the recording did. Each second holds every file and link read in the seconds before it too, read
/// again as it ends: the readers find the hardware only now and then, and a second of the tree replays alone, as a
/// rescan in it or a test that breaks one second finds it whole.
/// </summary>
internal sealed partial class RecordingKernel(IKernel kernel, string tree) : IKernel, IDisposable
{
    private readonly Dictionary<string, string> _links = new(StringComparer.Ordinal);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly HashSet<string> _kept = new(StringComparer.Ordinal);
    private int _second = -1;

    private string Second => Path.Combine(tree, _second.ToString(CultureInfo.InvariantCulture));

    // The readers read what the recording keeps, so the snapshots it is made with are the ones its replay gives.
    public string? Read(string path)
    {
        var text = kernel.Read(path) is { } read ? Serial().Replace(read, "") : null;
        if (text is not null)
        {
            Keep(RecordedKernel.Escaped(path), text);
            _known.Add(path);
            _kept.Add(path);
        }

        return text;
    }

    public IReadOnlyList<string> List(string folder) => kernel.List(folder);

    public string? Resolve(string path)
    {
        var target = kernel.Resolve(path);
        if (target is not null)
        {
            _links[path] = target;
        }

        return target;
    }

    public Moment? Now()
    {
        var moment = kernel.Now()?.Scrubbed();
        if (moment is not null)
        {
            Keep(KernelJson.MomentFile, KernelJson.Write(moment));
        }

        return moment;
    }

    public IReadOnlyList<GpuAnswer>? Nvml()
    {
        var gpus = kernel.Nvml();
        if (gpus is not null)
        {
            Keep(KernelJson.NvmlFile, KernelJson.Write(gpus));
        }

        return gpus;
    }

    public void LetGoOfNvml() => kernel.LetGoOfNvml();

    public void Next()
    {
        Flush();
        _second++;
        Directory.CreateDirectory(Second);
        kernel.Next();
    }

    public void Dispose() => Flush();

    private void Flush()
    {
        if (_second < 0)
        {
            return;
        }

        // A file that is gone stays out of the second, as it reads missing in it.
        foreach (var path in _known.Except(_kept, StringComparer.Ordinal).ToList())
        {
            if (kernel.Read(path) is { } text)
            {
                Keep(RecordedKernel.Escaped(path), Serial().Replace(text, ""));
            }
        }

        _kept.Clear();
        if (_links.Count > 0)
        {
            Keep(KernelJson.LinksFile, KernelJson.Write(_links));
        }
    }

    private void Keep(string relative, string text)
    {
        var file = Path.Combine(Second, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    // A Raspberry Pi's /proc/cpuinfo ends with its board's serial.
    [GeneratedRegex(@"^Serial\s*:.*\n?", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Serial();
}
