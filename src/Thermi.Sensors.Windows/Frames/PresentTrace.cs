using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Thermi.Core.Frames;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Etw;

namespace Thermi.Sensors.Windows.Frames;

/// <summary>A present as the trace tells of it: the process, when on the trace's clock in milliseconds, and what told.</summary>
internal readonly record struct TracedPresent(int Process, double At, PresentSource Source);

/// <summary>What tells of every present on the PC while it runs: ETW in the service, a fake in tests.</summary>
internal interface IPresentTrace : IDisposable
{
    /// <summary>Starts telling <paramref name="sink"/> of each present, from a thread of its own; false when it cannot.</summary>
    bool Start(Action<TracedPresent> sink);

    /// <summary>Whether it still tells: false once its session was stopped from outside, as by <c>logman stop</c>.</summary>
    bool Running { get; }
}

/// <summary>
/// A real-time ETW session, <c>thermi-frames</c> for the service and another for <c>--frames</c>, with only the present
/// events of DXGI, Direct3D 9 and the graphics kernel enabled, filtered by their ids so the kernel's many others never reach
/// it (docs/adr/0014-frames-from-etw.md). One thread runs <c>ProcessTrace</c>, which returns once the session stops. A
/// session of that name left by a crash is stopped first.
/// </summary>
internal sealed unsafe class PresentTrace(TimeProvider time, string sessionName = PresentTrace.SessionName) : IPresentTrace
{
    public const string SessionName = "thermi-frames";

    /// <summary>The session of <c>--frames</c>, apart, so a recording never stops the service's.</summary>
    public const string RecordingName = "thermi-frames-recording";

    private const uint ClockQpc = 1;
    // Every level and, for the runtimes, every keyword: the ids filter what comes, so no event is lost to a guess at either.
    private const byte LevelVerbose = 5;

    // EVENT_TRACE_USE_MS_FLUSH_TIMER, which Windows' metadata lacks: the flush timer counts milliseconds.
    private const uint MillisecondFlush = 0x10;

    // A session flushes its buffers this often, well inside the second a read waits for the trace to deliver (FramedSource.Lag).
    private const uint FlushMilliseconds = 250;
    private const ulong KernelPresentKeywords = 0x8000001;

    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    // DXGI's Present and PresentMultiplaneOverlay starts; Direct3D 9's Present start; the kernel's PresentHistory and
    // PresentHistoryDetailed starts, its BlitRect, by which alone some games, as Zwift in its menus, tell of their frames, and its Blit,
    // which only a recording asks for, to learn which presents reach it.
    private static readonly ushort[] DxgiEvents = [0x2a, 0x37];
    private static readonly ushort[] D3d9Events = [0x01];
    private static readonly ushort[] KernelEvents = [0xab, 0xd7, 0xa7, 0xa6];

    private static readonly Dictionary<(Guid Provider, ushort Id), PresentSource> Sources = new()
    {
        [(Dxgi, DxgiEvents[0])] = PresentSource.Dxgi,
        [(Dxgi, DxgiEvents[1])] = PresentSource.Dxgi,
        [(D3d9, D3d9Events[0])] = PresentSource.D3d9,
        [(DxgKrnl, KernelEvents[0])] = PresentSource.Kernel,
        [(DxgKrnl, KernelEvents[1])] = PresentSource.KernelDetailed,
        [(DxgKrnl, KernelEvents[2])] = PresentSource.KernelBlitRect,
        [(DxgKrnl, KernelEvents[3])] = PresentSource.KernelBlit,
    };

    private readonly double _msPerTick = 1000.0 / time.TimestampFrequency;
    private Action<TracedPresent>? _sink;
    private ulong _session;
    private PROCESSTRACE_HANDLE _trace = new(ulong.MaxValue);
    private GCHandle _self;
    private Thread? _thread;
    private volatile bool _running;

    public bool Running => _running;

    public bool Start(Action<TracedPresent> sink)
    {
        _sink = sink;
        Stop(0);
        var properties = Properties();
        try
        {
            ulong session;
            fixed (char* name = sessionName)
            {
                if (SensorPInvoke.StartTrace(&session, name, properties) != WIN32_ERROR.NO_ERROR)
                {
                    return false;
                }
            }

            _session = session;
        }
        finally
        {
            NativeMemory.Free(properties);
        }

        if (!Enable(Dxgi, ulong.MaxValue, DxgiEvents)
            || !Enable(D3d9, ulong.MaxValue, D3d9Events)
            || !Enable(DxgKrnl, KernelPresentKeywords, sessionName == RecordingName ? KernelEvents : KernelEvents[..3])
            || !Open())
        {
            Dispose();
            return false;
        }

        _running = true;
        _thread = new Thread(Process) { IsBackground = true, Name = sessionName };
        _thread.Start();
        return true;
    }

    public void Dispose()
    {
        if (_session != 0)
        {
            Stop(_session);
            _session = 0;
        }

        if (_trace.Value != ulong.MaxValue)
        {
            SensorPInvoke.CloseTrace(_trace);
            _trace = new PROCESSTRACE_HANDLE(ulong.MaxValue);
        }

        // A callback still running would read the handle freed: left allocated, a thread stuck past the wait costs one object.
        if (_thread?.Join(TimeSpan.FromSeconds(5)) is false)
        {
            return;
        }

        _thread = null;
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnEvent(EVENT_RECORD* record)
    {
        if (GCHandle.FromIntPtr((nint)record->UserContext).Target is PresentTrace trace)
        {
            trace.Take(record);
        }
    }

    private void Take(EVENT_RECORD* record)
    {
        ref var header = ref record->EventHeader;
        if (SourceOf(header.ProviderId, header.EventDescriptor.Id) is { } source)
        {
            _sink?.Invoke(new TracedPresent((int)header.ProcessId, header.TimeStamp * _msPerTick, source));
        }
    }

    /// <summary>
    /// What told of a present, by its provider and event id; null for any other event, as the session's own header or
    /// one a filter let through, which is never a frame.
    /// </summary>
    private static PresentSource? SourceOf(Guid provider, ushort id) => Sources.TryGetValue((provider, id), out var source) ? source : null;

    private void Process()
    {
        var handles = stackalloc PROCESSTRACE_HANDLE[1];
        handles[0] = _trace;
        _ = SensorPInvoke.ProcessTrace(handles, 1, null, null);
        _running = false;
    }

    // Only the events named, so the kernel's flips, vertical blanks and memory events never cost a callback.
    private bool Enable(Guid provider, ulong keywords, ushort[] events)
    {
        var size = sizeof(EVENT_FILTER_EVENT_ID) + ((events.Length - 1) * sizeof(ushort));
        var filter = (EVENT_FILTER_EVENT_ID*)NativeMemory.AllocZeroed((nuint)size);
        try
        {
            filter->FilterIn = true;
            filter->Count = (ushort)events.Length;
            for (var index = 0; index < events.Length; index++)
            {
                filter->Events[index] = events[index];
            }

            var descriptor = new EVENT_FILTER_DESCRIPTOR { Ptr = (ulong)filter, Size = (uint)size, Type = SensorPInvoke.EVENT_FILTER_TYPE_EVENT_ID };
            var parameters = new ENABLE_TRACE_PARAMETERS
            {
                Version = SensorPInvoke.ENABLE_TRACE_PARAMETERS_VERSION_2,
                EnableFilterDesc = &descriptor,
                FilterDescCount = 1,
            };
            return SensorPInvoke.EnableTraceEx2(
                _session,
                &provider,
                (uint)ENABLECALLBACK_ENABLED_STATE.EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                LevelVerbose,
                keywords,
                0,
                0,
                &parameters) == WIN32_ERROR.NO_ERROR;
        }
        finally
        {
            NativeMemory.Free(filter);
        }
    }

    private bool Open()
    {
        _self = GCHandle.Alloc(this);
        fixed (char* name = sessionName)
        {
            var logfile = new EVENT_TRACE_LOGFILEW { LoggerName = name, Context = (void*)GCHandle.ToIntPtr(_self) };
            logfile.Anonymous1.ProcessTraceMode =
                SensorPInvoke.PROCESS_TRACE_MODE_REAL_TIME | SensorPInvoke.PROCESS_TRACE_MODE_EVENT_RECORD | SensorPInvoke.PROCESS_TRACE_MODE_RAW_TIMESTAMP;
            logfile.Anonymous2.EventRecordCallback = &OnEvent;
            _trace = SensorPInvoke.OpenTrace(ref logfile);
        }

        return _trace.Value != ulong.MaxValue;
    }

    // The session by its id, or by its name for one left by a crash.
    private void Stop(ulong session)
    {
        var properties = Properties();
        try
        {
            fixed (char* name = sessionName)
            {
                _ = SensorPInvoke.ControlTrace(session, session == 0 ? name : default(PCWSTR), properties, EVENT_TRACE_CONTROL.EVENT_TRACE_CONTROL_STOP);
            }
        }
        finally
        {
            NativeMemory.Free(properties);
        }
    }

    // A real-time session on the performance counter's clock, flushed every 250 ms, its name in the room after it.
    private EVENT_TRACE_PROPERTIES* Properties()
    {
        var size = sizeof(EVENT_TRACE_PROPERTIES) + ((sessionName.Length + 1) * sizeof(char));
        var properties = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed((nuint)size);
        properties->Wnode.BufferSize = (uint)size;
        properties->Wnode.Flags = SensorPInvoke.WNODE_FLAG_TRACED_GUID;
        properties->Wnode.ClientContext = ClockQpc;
        properties->LogFileMode = SensorPInvoke.EVENT_TRACE_REAL_TIME_MODE | MillisecondFlush;
        properties->FlushTimer = FlushMilliseconds;
        properties->BufferSize = 64;
        properties->MinimumBuffers = 4;
        properties->MaximumBuffers = 16;
        properties->LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
        return properties;
    }
}
