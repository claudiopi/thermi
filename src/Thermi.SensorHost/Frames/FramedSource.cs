using System.Collections.Concurrent;
using Thermi.Core.Frames;
using Thermi.Core.Sensors;

namespace Thermi.SensorHost.Frames;

/// <summary>
/// The sensors with each second's frame slots added (docs/adr/0014-frames-from-etw.md), the sensors left to their owner to
/// close. The trace starts with the first read, so only while a client is connected, and stops once reads have stopped for
/// <see cref="Idle"/>. A trace that cannot start sends the slots without values, says why once, and is tried again after
/// <see cref="Retry"/>, as one stopped from outside is started again. The compositor's presents are left out: they are the
/// desktop's, never an app's.
/// </summary>
internal sealed class FramedSource : ISensorSource
{
    /// <summary>
    /// How far behind now a second closes: a real-time trace delivers its presents within a second, well after its flush,
    /// and a present that comes after its second closed is lost.
    /// </summary>
    public static readonly TimeSpan Lag = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Retry = TimeSpan.FromMinutes(1);

    // Often enough that the trace stops within a second of Idle.
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(1);

    private readonly ISensorSource _sensors;
    private readonly Func<IPresentTrace> _open;
    private readonly IProcessTable _processes;
    private readonly TimeProvider _time;
    private readonly Action<string> _say;
    private readonly ITimer _idle;
    private readonly Lock _gate = new();
    private FrameTally _tally = new();
    private IPresentTrace? _trace;
    private long _lastRead;
    private long? _failed;
    private bool _said;

    public FramedSource(ISensorSource sensors, Func<IPresentTrace> open, IProcessTable processes, TimeProvider time, Action<string> say)
    {
        _sensors = sensors;
        _open = open;
        _processes = processes;
        _time = time;
        _say = say;
        _idle = time.CreateTimer(_ => StopIfIdle(), null, IdleCheck, IdleCheck);
    }

    public async Task<Snapshot> ReadAsync(CancellationToken stop)
    {
        var snapshot = await _sensors.ReadAsync(stop);
        IReadOnlyList<FrameRate>? rates;
        lock (_gate)
        {
            _lastRead = _time.GetTimestamp();
            Begin();
            rates = _trace is null ? null : _tally.Close(Milliseconds(_lastRead) - Lag.TotalMilliseconds);
        }

        return snapshot with { Sensors = [.. snapshot.Sensors, .. FrameSlots.Of(rates)] };
    }

    public void Dispose()
    {
        _idle.Dispose();
        lock (_gate)
        {
            _trace?.Dispose();
            _trace = null;
        }
    }

    /// <summary>What a trace tells of, as the tally takes it: with its parent, and the compositor's left out.</summary>
    internal static Action<TracedPresent> Into(Action<Present> take, IProcessTable processes) => traced =>
    {
        var facts = processes.Of(traced.Process);
        if (facts is not { Compositor: true })
        {
            take(new Present(traced.Process, facts?.Parent ?? 0, traced.At, traced.Source));
        }
    };

    private void Begin()
    {
        if (_trace is { Running: false })
        {
            _trace.Dispose();
            _trace = null;
        }

        if (_trace is not null || (_failed is { } failed && _time.GetElapsedTime(failed) < Retry))
        {
            return;
        }

        var tally = new FrameTally();
        var trace = _open();
        if (trace.Start(Into(tally.Add, _processes)))
        {
            (_trace, _tally, _failed) = (trace, tally, null);
            return;
        }

        trace.Dispose();
        _failed = _time.GetTimestamp();
        if (!_said)
        {
            _said = true;
            _say("thermi-sensors could not start its trace of frames, so FPS and frame time read nothing. It tries again every minute.");
        }
    }

    private void StopIfIdle()
    {
        lock (_gate)
        {
            if (_trace is not null && _time.GetElapsedTime(_lastRead) >= Idle)
            {
                _trace.Dispose();
                _trace = null;
            }
        }
    }

    private double Milliseconds(long timestamp) => timestamp * 1000.0 / _time.TimestampFrequency;
}

/// <summary>
/// <c>thermi-sensors --frames &lt;file&gt; --seconds &lt;n&gt;</c>: every present for a while, one JSON line each, with ids,
/// times from the recording's start, what told of it and when it arrived, and no process's name, for tests and bug reports.
/// </summary>
internal static class FrameRecorder
{
    public static async Task<bool> RecordAsync(IPresentTrace trace, IProcessTable processes, TextWriter writer, int seconds, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);
        var start = Milliseconds(time.GetTimestamp(), time);
        var presents = new ConcurrentQueue<Present>();
        var into = FramedSource.Into(
            present => presents.Enqueue(present with
            {
                At = Math.Round(present.At - start, 3),
                Arrived = Math.Round(Milliseconds(time.GetTimestamp(), time) - start, 3),
            }),
            processes);
        if (!trace.Start(into))
        {
            return false;
        }

        using var tick = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        for (var second = 0; second < seconds; second++)
        {
            await tick.WaitForNextTickAsync();
            await WriteAsync(presents, writer);
        }

        trace.Dispose();
        await WriteAsync(presents, writer);
        return true;
    }

    private static double Milliseconds(long timestamp, TimeProvider time) => timestamp * 1000.0 / time.TimestampFrequency;

    private static async Task WriteAsync(ConcurrentQueue<Present> presents, TextWriter writer)
    {
        while (presents.TryDequeue(out var present))
        {
            await writer.WriteLineAsync(PresentJson.Write(present));
        }

        await writer.FlushAsync();
    }
}
