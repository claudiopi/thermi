using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;

namespace Thermi.Ui.Views;

/// <summary>
/// "Find it" in the CPU fan's Sensor row: Core's <see cref="FanFinder"/> fed every fresh snapshot once and ticked four
/// times a second on the injected clock, the load on every core while the finder asks for it, ended by the finder, a
/// cancel or 30 s at most. Its pick changes nothing: "Use it" on the page makes the item show it. Its result stays until
/// it is put away. Used on the window's thread alone.
/// </summary>
/// <param name="load">Spins every core until its token is cancelled; <see cref="Commands.CpuLoad.Run"/>, or the tests' own.</param>
/// <param name="time">The clock the finder, its ticks and the load's longest run follow.</param>
/// <param name="onTurn">Runs an action on the window's thread, where the clock's ticks are taken.</param>
/// <param name="changed">Tells the page that where the finder stands changed.</param>
internal sealed class WindowFinder(Action<CancellationToken> load, TimeProvider time, Action<Action> onTurn, Action changed) : IDisposable
{
    // Often enough that a silent service or the longest run ends the load well within a second.
    private static readonly TimeSpan TickEvery = TimeSpan.FromMilliseconds(250);

    private FanFinder? _finder;
    private ITimer? _ticks;
    private CancellationTokenSource? _stop;
    private Snapshot? _last;
    private long _loadedAt;

    private bool Running => _finder is { Result: null };

    /// <summary>How the last run ended, or null while it runs or before one.</summary>
    public FanFinderResult? Result => _finder?.Result;

    /// <summary>Starts on the snapshot shown now; nothing happens while it runs, or when it cannot start.</summary>
    public void Start(Snapshot? now)
    {
        if (now is null || Running || FanFinder.CannotStart(now) is not null)
        {
            return;
        }

        (_finder, _last) = (new FanFinder(now, time), now);
        _ticks = time.CreateTimer(_ => onTurn(Tick), null, TickEvery, TickEvery);
        changed();
    }

    /// <summary>A fresh snapshot, given once as it comes.</summary>
    public void Add(Snapshot snapshot)
    {
        if (_finder is { Result: null } finder)
        {
            _last = snapshot;
            finder.Add(snapshot);
            Follow(finder);
        }
    }

    /// <summary>Stops a run at once, or once it has ended, puts its result away.</summary>
    public void Cancel()
    {
        if (_finder is { Result: null } finder)
        {
            finder.Cancel();
            Follow(finder);
            return;
        }

        _finder = null;
        changed();
    }

    public FinderView View(Snapshot? now, Texts texts, IFormatProvider numbers, IReadOnlyDictionary<string, string>? own = null) => _finder switch
    {
        null => new FinderView(FinderPhase.Idle, null, Refusal: now is null ? null : FanFinder.CannotStart(now)),
        { Result: null } => Run(texts, numbers, own),
        { Result.Outcome: FanFinderOutcome.Cancelled } => new FinderView(FinderPhase.Cancelled, null),
        { Result: { Outcome: FanFinderOutcome.Found, Fan: { } fan }, Rising: var rising } => new FinderView(
            FinderPhase.Done,
            fan,
            Text: texts.Format(numbers, FanFinderOutcome.Found, NameOf(fan, own, texts)),
            Detail: rising is { } rose && rose.Fan == fan ? texts.Format(numbers, CatalogText.FanRose, Math.Round(rose.Rpm)) : null),
        { Result: { } result } => new FinderView(FinderPhase.Done, null, Text: texts.Of(result.Outcome)),
    };

    public void Dispose()
    {
        _finder?.Cancel();
        Stop();
    }

    private void Tick()
    {
        if (_finder is { Result: null } finder)
        {
            finder.Tick();
            Follow(finder);
        }
    }

    private void Follow(FanFinder finder)
    {
        if (finder.Loading && _stop is null)
        {
            _stop = new CancellationTokenSource(FanFinder.MaxRun, time);
            _loadedAt = time.GetTimestamp();
            load(_stop.Token);
        }

        if (finder.Result is not null)
        {
            Stop();
        }

        changed();
    }

    private void Stop()
    {
        _ticks?.Dispose();
        _ticks = null;
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
    }

    // Waiting for 3 s of readings, then the load, how far it has run and the fan rising most.
    private FinderView Run(Texts texts, IFormatProvider numbers, IReadOnlyDictionary<string, string>? own)
    {
        if (_stop is null)
        {
            return new FinderView(FinderPhase.Running, null, Progress: 0, Text: texts.Of(CatalogText.FanFinderWaiting));
        }

        var text = texts.Of(CatalogText.FanFinderLoading);
        if (_finder?.Rising is { } rising)
        {
            text += " · " + texts.Format(
                numbers,
                CatalogText.FanRising,
                NameOf(rising.Fan, own, texts),
                Math.Round(rising.Rpm),
                Math.Round(rising.Share * 100));
        }

        return new FinderView(FinderPhase.Running, null, Progress: Math.Min(1, time.GetElapsedTime(_loadedAt) / FanFinder.MaxRun), Text: text);
    }

    // The fan as the catalog and the Sensor dropdown name it, so one fan never reads by two names.
    private string NameOf(string fan, IReadOnlyDictionary<string, string>? own, Texts texts) =>
        _last is { } snapshot && SensorMap.Find(snapshot, fan) is { } sensor
            ? string.Create(CultureInfo.InvariantCulture, $"{SensorNames.Of(sensor, fan, own, texts).Shown} · {sensor.Hardware}")
            : fan;
}
