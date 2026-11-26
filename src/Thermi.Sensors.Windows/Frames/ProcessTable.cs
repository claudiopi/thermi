using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ProcessSnapshotting;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

namespace Thermi.Sensors.Windows.Frames;

/// <summary>What the frames need of a process: its parent's id, and whether it is the desktop's compositor.</summary>
internal readonly record struct ProcessFacts(int Parent, bool Compositor)
{
    /// <summary>The facts of a process from its parent's id and its program's name or path.</summary>
    public static ProcessFacts Of(int parent, string program) => new(parent, Path.GetFileName(program).Equals("dwm.exe", StringComparison.OrdinalIgnoreCase));
}

/// <summary>The processes running now, by id: Windows' own in the service, a fake in tests.</summary>
internal interface IProcessTable : IDisposable
{
    /// <summary>What is known of <paramref name="process"/>, or null for one that has gone or cannot be opened.</summary>
    ProcessFacts? Of(int process);

    /// <summary>Lets every held process go, as when the trace stops: one that ends before the next trace must not keep its id.</summary>
    void Release();
}

/// <summary>A process held open, so Windows gives its id to no other while it is held.</summary>
internal interface IHeldProcess : IDisposable
{
    ProcessFacts Facts { get; }

    bool Exited { get; }
}

/// <summary>
/// The processes that present, each read once and held open while it runs: Windows' list of every process costs some
/// 12 ms of CPU on a busy PC, too much to take each second. A held process's id cannot go to another, so its facts never
/// name a process that took its id. One that cannot be opened, as the compositor is to a run without admin rights,
/// is looked up in the list, taken again when asked once it is a second old, and opened again only after the second's
/// sweep, not at each of its presents. Names are read only to tell the compositor, and never kept.
/// </summary>
internal sealed class ProcessTable(TimeProvider time, Func<int, IHeldProcess?> open, Func<Dictionary<int, ProcessFacts>> list) : IProcessTable
{
    private static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<int, IHeldProcess> _held = [];
    private readonly HashSet<int> _unopened = [];
    private long _swept = time.GetTimestamp();
    private Dictionary<int, ProcessFacts> _listed = [];
    private long? _taken;

    public ProcessTable(TimeProvider time)
        : this(time, HeldProcess.Open, ProcessList.Take)
    {
    }

    public ProcessFacts? Of(int process)
    {
        lock (_gate)
        {
            if (time.GetElapsedTime(_swept) >= SweepEvery)
            {
                Sweep();
            }

            if (!_held.TryGetValue(process, out var held))
            {
                if (_unopened.Contains(process) || open(process) is not { } opened)
                {
                    _unopened.Add(process);
                    return Listed(process);
                }

                _held[process] = held = opened;
            }

            return held.Facts;
        }
    }

    public void Dispose() => Release();

    public void Release()
    {
        lock (_gate)
        {
            foreach (var held in _held.Values)
            {
                held.Dispose();
            }

            _held.Clear();
            _unopened.Clear();
        }
    }

    private ProcessFacts? Listed(int process)
    {
        if (_taken is not { } taken || time.GetElapsedTime(taken) >= SweepEvery)
        {
            _listed = list();
            _taken = time.GetTimestamp();
        }

        return _listed.TryGetValue(process, out var facts) ? facts : null;
    }

    private void Sweep()
    {
        _swept = time.GetTimestamp();
        _unopened.Clear();
        foreach (var (process, held) in _held.Where(each => each.Value.Exited).ToList())
        {
            held.Dispose();
            _held.Remove(process);
        }
    }
}

/// <summary>A process of the PC, opened for its parent, its name and its end, which the service may ask of any process.</summary>
internal sealed class HeldProcess(SafeFileHandle handle, ProcessFacts facts) : IHeldProcess
{
    public ProcessFacts Facts { get; } = facts;

    public bool Exited => SensorPInvoke.WaitForSingleObject(handle, 0) != WAIT_EVENT.WAIT_TIMEOUT;

    /// <summary>The process of that id, or null for one that has gone or cannot be opened.</summary>
    public static HeldProcess? Open(int process)
    {
        var handle = SensorPInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_SYNCHRONIZE, false, (uint)process);
        try
        {
            if (!handle.IsInvalid && Read(handle) is { } facts)
            {
                var held = new HeldProcess(handle, facts);
                handle = null;
                return held;
            }

            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    public void Dispose() => handle.Dispose();

    private static unsafe ProcessFacts? Read(SafeFileHandle handle)
    {
        if (SensorPInvoke.PssCaptureSnapshot(handle, PSS_CAPTURE_FLAGS.PSS_CAPTURE_NONE, 0, out var snapshot) != (uint)WIN32_ERROR.NO_ERROR)
        {
            return null;
        }

        try
        {
            var info = default(PSS_PROCESS_INFORMATION);
            var read = SensorPInvoke.PssQuerySnapshot(
                snapshot, PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_PROCESS_INFORMATION, &info, (uint)sizeof(PSS_PROCESS_INFORMATION));
            if (read != (uint)WIN32_ERROR.NO_ERROR)
            {
                return null;
            }

            return ProcessFacts.Of((int)info.ParentProcessId, info.ImageFileName.ToString());
        }
        finally
        {
            _ = SensorPInvoke.PssFreeSnapshot(SensorPInvoke.GetCurrentProcess(), snapshot);
        }
    }
}

/// <summary>Windows' list of every process, for one the service cannot open.</summary>
internal static class ProcessList
{
    public static unsafe Dictionary<int, ProcessFacts> Take()
    {
        var known = new Dictionary<int, ProcessFacts>();
        using var snapshot = SensorPInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            return known;
        }

        var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        for (var more = SensorPInvoke.Process32FirstW(snapshot, ref entry); more; more = SensorPInvoke.Process32NextW(snapshot, ref entry))
        {
            known[(int)entry.th32ProcessID] = ProcessFacts.Of((int)entry.th32ParentProcessID, entry.szExeFile.ToString());
        }

        return known;
    }
}
