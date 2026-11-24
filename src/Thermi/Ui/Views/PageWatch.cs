using Thermi.Core.Sensors;
using Thermi.Core.Tray;
using Thermi.Overlay;

namespace Thermi.Ui.Views;

/// <summary>
/// What the bridge keeps between the page's reads, so a second's readings cost only what they change. Whether the first
/// readings came, which the page's first reads wait for, so a window opened before them never shows as missing what the
/// overlay already shows; they wait no longer than the service's data takes to go stale. Which sensors the last snapshot
/// held, and the service's state, whose change can rename an unnamed item or crowd an overlay and which the page reads
/// only at a change, not with each second's values. And the overlay's hotkey status, which also says
/// whether it runs: asking waits on the overlay, so it is asked off the window's thread, and only when the overlay came or
/// went, a while after the settings changed, as the overlay registers them again, or when asked to. Used on the window's
/// thread alone.
/// </summary>
internal sealed class PageWatch : IDisposable
{
    // Long enough for the overlay to read the changed settings and register their hotkeys again.
    private static readonly TimeSpan HotkeysSettle = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan FirstPoll = TimeSpan.FromMilliseconds(250);

    // A second's snapshot late by half a second: the service stopped, or its pipe went quiet, and no snapshot says so.
    private static readonly TimeSpan QuietAfter = TimeSpan.FromMilliseconds(1500);

    private readonly WindowEnvironment _environment;
    private readonly TimeProvider _time;
    private readonly Action<Action> _onTurn;
    private readonly Action _changed;
    private readonly Action _quiet;
    private readonly List<Action> _afterFirst = [];
    private readonly ITimer _waiting;
    private readonly ITimer _quietCheck;
    private readonly long _opened;
    private long _arrived;
    private bool _first;
    private int? _sensors;
    private long? _hotkeysDue;
    private bool _overlay;
    private bool _answered;

    /// <param name="environment">The sensors, and how the overlay is asked for its hotkeys.</param>
    /// <param name="time">The clock of the first readings' wait and of asking after a change.</param>
    /// <param name="onTurn">Runs an action on the window's thread.</param>
    /// <param name="changed">Tells the page, on the window's thread, that the hotkey status changed.</param>
    /// <param name="quiet">Runs on the window's thread each second no snapshot came, as one coming would.</param>
    public PageWatch(WindowEnvironment environment, TimeProvider time, Action<Action> onTurn, Action changed, Action quiet)
    {
        _environment = environment;
        _time = time;
        _onTurn = onTurn;
        _changed = changed;
        _quiet = quiet;
        _opened = _arrived = time.GetTimestamp();
        _sensors = Signature(environment.Sensors.Latest, environment.Sensors.State);
        _waiting = time.CreateTimer(_ => onTurn(Waited), null, TimeSpan.Zero, FirstPoll);
        _quietCheck = time.CreateTimer(_ => onTurn(Quieted), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // Off the window's thread from the start too: an overlay that is slow to answer would hold the window's first
        // frame for as long. The answer comes while the page loads, long before it reads.
        AskHotkeys();
    }

    /// <summary>What <paramref name="read"/> answers once the first readings came, or none will soon: at once when they have.</summary>
    public Task<T> AfterFirst<T>(Func<T> read)
    {
        var answer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Answer()
        {
            try
            {
                answer.SetResult(read());
            }
#pragma warning disable CA1031 // Not handled: the page's call fails with it, as it would have without the wait.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                answer.SetException(ex);
            }
        }

        if (_first)
        {
            Answer();
        }
        else
        {
            _afterFirst.Add(Answer);
        }

        return answer.Task;
    }

    /// <summary>The overlay's last answer about its hotkeys, null while none runs.</summary>
    public long? Hotkeys { get; private set; }

    /// <summary>A snapshot came: true when it holds other sensors than the last one did.</summary>
    public bool Arrived(Snapshot? snapshot)
    {
        _arrived = _time.GetTimestamp();
        Waited();
        var running = OverlayHost.Running();
        if (running != _overlay || (_hotkeysDue is { } due && _time.GetTimestamp() >= due))
        {
            _overlay = running;
            AskHotkeys();
        }

        var sensors = Signature(snapshot, _environment.Sensors.State);
        var changed = sensors != _sensors;
        _sensors = sensors;
        return changed;
    }

    /// <summary>The settings changed: the overlay's hotkeys are asked for once it has registered them again.</summary>
    public void Edited() => _hotkeysDue = _time.GetTimestamp() + (long)(HotkeysSettle.TotalSeconds * _time.TimestampFrequency);

    /// <summary>Asks the overlay for its hotkeys off the window's thread; the page is told if the answer changed.</summary>
    public void AskHotkeys()
    {
        _hotkeysDue = null;
        _ = Task.Run(() =>
        {
            var answer = _environment.HotkeyStatus();
            _onTurn(() => Answered(answer));
        });
    }

    public void Dispose()
    {
        _waiting.Dispose();
        _quietCheck.Dispose();
    }

    // Sensors by their ids, as a snapshot names them, and the service's state, which the page reads with them; null for none.
    private static int? Signature(Snapshot? snapshot, ServiceState state)
    {
        if (snapshot is null)
        {
            return null;
        }

        var hash = new HashCode();
        hash.Add(state);
        foreach (var sensor in snapshot.Sensors)
        {
            hash.Add(sensor.Id, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private void Answered(long? answer)
    {
        if (!_answered)
        {
            (_answered, _overlay) = (true, answer is not null);
        }

        if (answer != Hotkeys)
        {
            Hotkeys = answer;
            _changed();
        }
    }

    private void Quieted()
    {
        if (_time.GetElapsedTime(_arrived) >= QuietAfter)
        {
            _quiet();
        }
    }

    private void Waited()
    {
        var sensors = _environment.Sensors;
        if (_first || (sensors.Latest is null && sensors.State == ServiceState.Running && _time.GetElapsedTime(_opened) < LatestSnapshot.MaxAge))
        {
            return;
        }

        _first = true;
        _waiting.Dispose();
        foreach (var read in _afterFirst)
        {
            read();
        }

        _afterFirst.Clear();
    }
}
