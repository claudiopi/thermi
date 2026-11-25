using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace Thermi.Sensors.Windows.Frames;

/// <summary>What the frames need of a process: its parent's id, and whether it is the desktop's compositor.</summary>
internal readonly record struct ProcessFacts(int Parent, bool Compositor);

/// <summary>The processes running now, by id: Windows' own list in the service, a fake in tests.</summary>
internal interface IProcessTable
{
    /// <summary>What is known of <paramref name="process"/>, or null for one that has gone or is not yet listed.</summary>
    ProcessFacts? Of(int process);
}

/// <summary>
/// Windows' list of processes, taken again when asked once it is a second old, so a process that just started finds its
/// parent within a second, a reused id never keeps a gone process's facts longer, and a burst of presents costs one list.
/// Names are read only to tell the compositor, and never kept.
/// </summary>
internal sealed class ProcessTable(TimeProvider time) : IProcessTable
{
    private const string Compositor = "dwm.exe";

    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private Dictionary<int, ProcessFacts> _known = [];
    private long? _taken;

    public ProcessFacts? Of(int process)
    {
        lock (_gate)
        {
            if (_taken is not { } taken || time.GetElapsedTime(taken) >= Fresh)
            {
                _known = List();
                _taken = time.GetTimestamp();
            }

            return _known.TryGetValue(process, out var facts) ? facts : null;
        }
    }

    private static unsafe Dictionary<int, ProcessFacts> List()
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
            var name = entry.szExeFile.ToString();
            known[(int)entry.th32ProcessID] = new ProcessFacts((int)entry.th32ParentProcessID, name.Equals(Compositor, StringComparison.OrdinalIgnoreCase));
        }

        return known;
    }
}
