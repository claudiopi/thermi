using Thermi.Core.Look;

namespace Thermi.Core.Sensors;

/// <summary>
/// The values read since the window opened, one a second, up to ten minutes: the last minute for sparklines and
/// all ten for the threshold histogram. Each series is a fixed ring of slots, so memory never grows with time. A
/// second without a reading (the service stopped, or its data went stale) stays empty, never filled with the last value.
/// The overlay's items keep a minute the same way, in <see cref="ItemHistory"/>.
/// </summary>
public sealed class SensorHistory
{
    public static readonly TimeSpan Kept = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Sparkline = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly int _slots;
    private readonly Dictionary<string, double[]> _series = new(StringComparer.Ordinal);
    private long _second = -1;
    private long _readAt;

    public SensorHistory(TimeProvider time)
        : this(time, Kept)
    {
    }

    internal SensorHistory(TimeProvider time, TimeSpan kept)
    {
        _time = time;
        _slots = (int)kept.TotalSeconds;
    }

    /// <summary>Keeps one fresh reading of every sensor or metric, under the id it is asked for by. Stale data is not added.</summary>
    public void Add(IReadOnlyDictionary<string, Reading> readings)
    {
        var second = Now();
        foreach (var series in _series.Values)
        {
            Clear(series, second);
        }

        foreach (var (id, reading) in readings)
        {
            if (!_series.TryGetValue(id, out var series))
            {
                series = new double[_slots];
                Array.Fill(series, double.NaN);
                _series[id] = series;
            }

            series[Slot(second)] = reading.Usable ?? double.NaN;
        }

        _second = second;
        _readAt = _time.GetTimestamp();
    }

    /// <summary>The last minute, a value a second and oldest first, from the first reading on; null where there was none.</summary>
    public IReadOnlyList<double?> LastMinute(string id) => Values(id, (int)Sparkline.TotalSeconds);

    /// <summary>The values of the last <paramref name="seconds"/> seconds, as <see cref="LastMinute"/> gives them.</summary>
    internal IReadOnlyList<double?> Last(string id, int seconds) => Values(id, Math.Min(seconds, _slots));

    /// <summary>Drops every series but those of <paramref name="ids"/>, so memory follows what is still asked for.</summary>
    internal void Keep(IReadOnlySet<string> ids)
    {
        foreach (var id in _series.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            _series.Remove(id);
        }
    }

    /// <summary>
    /// How many seconds of the last ten minutes the value spent at each of <paramref name="levels"/> equal levels from 0 to
    /// 100, <paramref name="offset"/> added to each, as an item's offset is to the value it meters.
    /// </summary>
    public IReadOnlyList<int> Histogram(string id, int levels, double offset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        var seconds = new int[levels];
        foreach (var value in Values(id, _slots).OfType<double>())
        {
            seconds[Math.Clamp((int)Math.Floor((value + offset) / 100 * levels), 0, levels - 1)]++;
        }

        return seconds;
    }

    /// <summary>The lowest and the highest value of the last ten minutes, or null when there was none.</summary>
    public (double Min, double Max)? Range(string id)
    {
        var values = Values(id, _slots).OfType<double>().ToList();
        return values.Count == 0 ? null : (values.Min(), values.Max());
    }

    private List<double?> Values(string id, int count)
    {
        var now = _second < 0 ? -1 : _second + (long)Math.Floor(_time.GetElapsedTime(_readAt).TotalSeconds);
        var first = Math.Max(0, now - count + 1);
        _series.TryGetValue(id, out var series);
        var values = new List<double?>();
        for (var second = first; second <= now; second++)
        {
            var value = series is not null && second <= _second ? series[Slot(second)] : double.NaN;
            values.Add(double.IsNaN(value) ? null : value);
        }

        return values;
    }

    // Seconds are counted from the last reading, not from a fixed start, so a service whose second drifts a little
    // never skips or repeats one; a reading within half a second of the last replaces it.
    private long Now() => _second < 0 ? 0 : _second + (long)Math.Round(_time.GetElapsedTime(_readAt).TotalSeconds, MidpointRounding.AwayFromZero);

    // The seconds since the last reading had none, and their slots still hold what was read a whole ring before.
    private void Clear(double[] series, long second)
    {
        for (var each = Math.Max(_second + 1, second - _slots + 1); each <= second; each++)
        {
            series[Slot(each)] = double.NaN;
        }
    }

    private int Slot(long second) => (int)(second % _slots);
}
