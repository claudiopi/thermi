using Thermi.Core.Translations;

namespace Thermi.Core.Sensors;

/// <summary>Why the fan finder cannot start. The window says each in words of its own.</summary>
[Translated]
public enum FanFinderRefusal
{
    /// <summary>Only the GPUs report fans: the board reports none.</summary>
    NoBoardFans,

    /// <summary>Only the GPUs report fans, and the CPU temperature reads zero: the PawnIO driver may be missing.</summary>
    DriverMayBeMissing,
}

/// <summary>How a run of the fan finder ended. The window says each in words of its own.</summary>
[Translated]
public enum FanFinderOutcome
{
    /// <summary>One fan rose clearly, and <see cref="FanFinderResult.Fan"/> names it.</summary>
    Found,

    /// <summary>
    /// No fan rose enough. The window adds that the fans may follow a curve set by fan-control software,
    /// and that the fan can be picked by hand in the catalog.
    /// </summary>
    NoFanRose,

    /// <summary>A fan rose enough, but another rose more than half as much.</summary>
    TwoRoseAlike,

    /// <summary>The CPU load, averaged after its first seconds, stayed under 80 %, so the fans had nothing to answer.</summary>
    LoadDidNotReachTheCpu,

    /// <summary>The readings stopped during the run.</summary>
    ReadingsStopped,

    Cancelled,
}

/// <summary>How the finder ended, and the id of the fan it found, as <see cref="SensorMap.Named"/> gives it.</summary>
public sealed record FanFinderResult(FanFinderOutcome Outcome, string? Fan = null);

/// <summary>How much a fan's speed, averaged over its last 3 s, rose over its 3 s before the load: in RPM, and as a share of where it was.</summary>
public sealed record FanRise(string Fan, double Rpm, double Share);

/// <summary>
/// Finds the fan that cools the CPU: it waits for 3 s of readings, asks for a load on every core, and watches which
/// fan answers it, during the load and for <see cref="Afterwards"/> after it. Every fan outside the GPUs is a candidate: a
/// GPU's never cools the CPU. The load itself is the window's, which keeps it running while <see cref="Loading"/> says so;
/// the finder decides on the snapshots it is given and the injected clock alone, so a recorded series replays the same
/// every time. Give it every new snapshot once, with <see cref="Add"/>, and call <see cref="Tick"/> often, so a service
/// that goes silent stops the load too.
/// </summary>
public sealed class FanFinder
{
    public static readonly TimeSpan Baseline = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan MinRun = TimeSpan.FromSeconds(8);

    public static readonly TimeSpan MaxRun = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the readings may be missing before they count as gone: a pause of a few seconds, as a PC busy on every
    /// core may make, only leaves the averages fewer readings. Gone during the load, they end it, since the load may be
    /// what holds them back, and get as long again to come back while the fans are watched. Halfway between whole
    /// seconds, so jitter never decides.
    /// </summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(8.5);

    /// <summary>
    /// How long it keeps watching the fans once the load has ended: a fan-control curve slow to answer, as on the owner's
    /// PC, raises the fan up to 12 s after the load, and the fan then meets the rule for 3 s.
    /// </summary>
    public static readonly TimeSpan Afterwards = TimeSpan.FromSeconds(15);

    // Each average spans 3 s, and a fan meets the rule for as long before it is picked.
    private static readonly TimeSpan Span = TimeSpan.FromSeconds(3);

    // The load takes a moment to reach every core, so its first seconds do not count against it.
    private static readonly TimeSpan LoadSettles = TimeSpan.FromSeconds(2);

    private const double MinRise = 200;
    private const double MinRiseShare = 0.2;
    private const double MinLoad = 80;

    private static readonly Dictionary<string, string> NoSources = new(StringComparer.Ordinal);

    private readonly TimeProvider _time;
    private readonly long _startedAt;
    private readonly List<Sample> _samples = [];
    private long _readAt;
    private long? _loadAt;
    private long? _loadEndedAt;
    private bool _readingsStopped;
    private (string Fan, long Since)? _leader;

    /// <summary>Starts with the snapshot the window shows now, which has to pass <see cref="CannotStart"/>.</summary>
    public FanFinder(Snapshot snapshot, TimeProvider time)
    {
        if (CannotStart(snapshot) is { } refusal)
        {
            throw new ArgumentException($"The fan finder cannot start: {refusal}.", nameof(snapshot));
        }

        _time = time;
        _startedAt = _readAt = time.GetTimestamp();
    }

    public bool Loading => _loadAt is not null && _loadEndedAt is null && Result is null;

    /// <summary>True once the load has ended while the finder still watches the fans, for <see cref="Afterwards"/> at most.</summary>
    public bool Watching => _loadEndedAt is not null && Result is null;

    /// <summary>How the run ended, or null while it runs.</summary>
    public FanFinderResult? Result { get; private set; }

    /// <summary>The fan that has risen most so far since the load started, whether or not it meets the rule yet; null before any rose.</summary>
    public FanRise? Rising { get; private set; }

    /// <summary>Why the finder cannot start on this snapshot, or null when it can: it needs a fan outside the GPUs.</summary>
    public static FanFinderRefusal? CannotStart(Snapshot snapshot) =>
        snapshot.Sensors.Any(sensor => sensor.Type == nameof(SensorKind.Fan) && !SensorMap.IsGpu(sensor)) ? null
        : SensorMap.DriverMayBeMissing(snapshot) ? FanFinderRefusal.DriverMayBeMissing
        : FanFinderRefusal.NoBoardFans;

    public void Add(Snapshot snapshot)
    {
        var now = _time.GetTimestamp();
        if (Ended(now))
        {
            return;
        }

        _readAt = now;
        _samples.Add(Sample.Of(snapshot, now));
        if (_loadAt is { } loadAt)
        {
            Rising = Rises(loadAt, now).Where(rise => rise.Rpm > 0).MaxBy(rise => rise.Rpm) is { } most
                ? new FanRise(most.Fan, most.Rpm, most.Before > 0 ? most.Rpm / most.Before : 0)
                : null;
            Follow(loadAt, now);
        }
        else if (Since(_startedAt, now) >= Baseline)
        {
            _loadAt = now;
        }
    }

    /// <summary>Ends the load when the readings stopped or it has run its longest, and the run once the fans have been watched.</summary>
    public void Tick()
    {
        var now = _time.GetTimestamp();
        if (!Ended(now) && _loadAt is { } loadAt)
        {
            Decide(loadAt, now, Leader(loadAt, now));
        }
    }

    public void Cancel() => Result ??= new FanFinderResult(FanFinderOutcome.Cancelled);

    private bool Ended(long now)
    {
        var heard = _loadEndedAt is { } ended && ended > _readAt ? ended : _readAt;
        if (Result is null && Since(heard, now) > MaxGap)
        {
            if (Loading)
            {
                (_loadEndedAt, _readingsStopped) = (now, true);
            }
            else
            {
                Result = new FanFinderResult(FanFinderOutcome.ReadingsStopped);
            }
        }

        return Result is not null;
    }

    private void Follow(long loadAt, long now)
    {
        if (Since(loadAt, now) < Span)
        {
            return;
        }

        var leader = Leader(loadAt, now);
        _leader = leader is null ? null : _leader is { } kept && kept.Fan == leader ? kept : (leader, now);
        Decide(loadAt, now, leader);
    }

    // A fan that has met the rule for 3 s in a row ends the run, during the load once past its minimum, or after it. At
    // the load's longest the last 3 s pick a fan, or the load ends; once the fans have been watched, the last 3 s decide.
    private void Decide(long loadAt, long now, string? leader)
    {
        if (_loadEndedAt is null && Since(loadAt, now) >= MaxRun)
        {
            if (leader is not null)
            {
                Result = new FanFinderResult(FanFinderOutcome.Found, leader);
                return;
            }

            _loadEndedAt = now;
        }

        var watched = _loadEndedAt is { } ended ? Since(ended, now) : (TimeSpan?)null;
        if ((watched is not null || Since(loadAt, now) >= MinRun) && _leader is { } met && met.Fan == leader && Since(met.Since, now) >= Span)
        {
            Result = new FanFinderResult(FanFinderOutcome.Found, met.Fan);
        }
        else if (_loadEndedAt is { } end && watched >= Afterwards)
        {
            Result = Judge(loadAt, end);
        }
    }

    // The last 3 s of readings decide, though the last came a few seconds before the watch ended.
    private FanFinderResult Judge(long loadAt, long loadEndedAt)
    {
        if (Leader(loadAt, _readAt) is { } leader)
        {
            return new FanFinderResult(FanFinderOutcome.Found, leader);
        }

        var loads = _samples
            .Where(sample => Since(loadAt, sample.At) > LoadSettles && sample.At <= loadEndedAt)
            .Select(sample => sample.Load)
            .OfType<double>()
            .ToList();
        return _readingsStopped ? new FanFinderResult(FanFinderOutcome.ReadingsStopped)
            : loads.Count == 0 || loads.Average() < MinLoad ? new FanFinderResult(FanFinderOutcome.LoadDidNotReachTheCpu)
            : Rises(loadAt, _readAt).Any(Rose) ? new FanFinderResult(FanFinderOutcome.TwoRoseAlike)
            : new FanFinderResult(FanFinderOutcome.NoFanRose);
    }

    // The fan that rose enough, and at least twice as many RPM as any other.
    private string? Leader(long loadAt, long now)
    {
        var rises = Rises(loadAt, now);
        var best = rises.MaxBy(rise => rise.Rpm);
        return best is not null && Rose(best) && rises.All(other => other == best || other.Rpm * 2 <= best.Rpm) ? best.Fan : null;
    }

    private static bool Rose(Rise rise) => rise.Rpm >= MinRise && rise.Rpm >= rise.Before * MinRiseShare;

    // Each fan's average over the last 3 s against its average over the 3 s before the load; a fan without a value in either has no rise.
    private List<Rise> Rises(long loadAt, long now)
    {
        var before = Averages(_samples.Where(sample => sample.At <= loadAt && Since(sample.At, loadAt) < Span));
        var last = Averages(_samples.Where(sample => sample.At > loadAt && Since(sample.At, now) < Span));
        return [.. before.Where(fan => last.ContainsKey(fan.Key)).Select(fan => new Rise(fan.Key, last[fan.Key] - fan.Value, fan.Value))];
    }

    private static Dictionary<string, double> Averages(IEnumerable<Sample> samples) =>
        samples.SelectMany(sample => sample.Fans)
            .Where(fan => fan.Value is not null)
            .GroupBy(fan => fan.Key, StringComparer.Ordinal)
            .ToDictionary(fan => fan.Key, fan => fan.Average(each => each.Value!.Value), StringComparer.Ordinal);

    private TimeSpan Since(long from, long to) => _time.GetElapsedTime(from, to);

    private sealed record Rise(string Fan, double Rpm, double Before);

    private sealed record Sample(long At, double? Load, IReadOnlyDictionary<string, double?> Fans)
    {
        public static Sample Of(Snapshot snapshot, long at) => new(
            at,
            SensorMap.Read(snapshot, NoSources)[Metrics.CpuLoad.Id].Usable,
            SensorMap.Named(snapshot)
                .Where(named => named.Sensor.Type == nameof(SensorKind.Fan) && !SensorMap.IsGpu(named.Sensor))
                .ToDictionary(named => named.Id, named => new Reading(SensorKind.Fan, named.Sensor.Value).Usable, StringComparer.Ordinal));
    }
}
