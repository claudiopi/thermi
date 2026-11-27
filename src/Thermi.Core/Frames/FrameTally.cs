using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Thermi.Core.Sensors;

namespace Thermi.Core.Frames;

/// <summary>Which event told of a present: the graphics runtime's, or the graphics kernel's beneath it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PresentSource>))]
public enum PresentSource
{
    /// <summary>DXGI's Present: Direct3D 10 to 12, and Vulkan or OpenGL presented through DXGI.</summary>
    Dxgi,

    /// <summary>Direct3D 9's Present.</summary>
    D3d9,

    /// <summary>The kernel's present history, which every present that flips or is composed reaches, whatever its API.</summary>
    Kernel,

    /// <summary>The kernel's detailed present history, which some Windows builds send beside the plain one or instead of it.</summary>
    KernelDetailed,

    /// <summary>The kernel's copy of a frame to the screen, recorded to learn which presents reach it, never counted.</summary>
    KernelBlit,

    /// <summary>
    /// The kernel's copy of a frame into its window, the only event that tells of some games' frames, as of Zwift's in its
    /// menus.
    /// </summary>
    KernelBlitRect,
}

/// <summary>
/// A frame a process presented: its id, its parent's, when, in milliseconds on the trace's clock, what told of it, and in a
/// recording, when the trace delivered it, on the same clock, which tells how long a second must wait for its presents.
/// </summary>
public readonly record struct Present(
    int Process,
    int Parent,
    double At,
    PresentSource Source = PresentSource.Dxgi,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Arrived = null);

/// <summary>
/// Presents as <c>thermi-sensors --frames</c> records them, one JSON line each, and as tests replay them: ids and times,
/// never a process's name.
/// </summary>
public static class PresentJson
{
    public static string Write(Present present) => JsonSerializer.Serialize(present, CoreJson.Default.Present);

    /// <summary>A present from one line, or null for anything else.</summary>
    public static Present? Read(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, CoreJson.Default.Present);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// A process's last second of frames: how many it presented a second, and the longest wait for one of them, the wait
/// still going at the second's end included, in milliseconds; and its 1 % and 0.1 % lows, the frames a second of its
/// slowest frames of the last minute, or null where they are not known, as from a service of an earlier version.
/// </summary>
public readonly record struct FrameRate(
    int Process,
    int Parent,
    double Fps,
    double FrameTime,
    double? OnePercentLow = null,
    double? PointOnePercentLow = null);

/// <summary>
/// Turns presents into each second's frame rates, as the sensor service reads them (docs/adr/0014-frames-from-etw.md).
/// Presents come from the trace's thread and seconds close on the reading's, so both take the same lock. A trace delivers
/// presents late and not quite in order, so each is kept until the second that holds it closes. A runtime's present
/// reaches the kernel too, which may tell of it by both of its histories or by a copy into its window, so each source is
/// counted apart and a second takes the one that told of most, the earliest of those that tie: a frame told twice counts
/// once, and a game that changes how it presents, as Zwift does between a ride and its menus, keeps its count.
/// </summary>
public sealed class FrameTally
{
    /// <summary>How many processes a second's rates name at most, the ones that presented most.</summary>
    public const int Slots = 8;

    /// <summary>How long a process that stopped presenting is kept at 0 FPS, as a game while it loads, in milliseconds.</summary>
    public const double KeptFor = 5000;

    /// <summary>The longest frame time told: a wait of a second or more is a freeze, and reads as one.</summary>
    public const double LongestFrame = 999.9;

    /// <summary>
    /// How many frames a second, for <see cref="SteadyFor"/> seconds in a row, a process presents before its rates are told:
    /// a game presents on and on, while a window that redraws now and then, as a terminal or thermi's own, never does.
    /// </summary>
    public const double SteadyFps = 10;

    /// <summary>How many seconds in a row a process presents at <see cref="SteadyFps"/> before its rates are told.</summary>
    public const int SteadyFor = 3;

    /// <summary>How far back the lows look, in milliseconds.</summary>
    public const double LowsOver = 60_000;

    /// <summary>
    /// How many frames a process keeps for its lows at most, a minute at 500 FPS: an app that presents faster has them over
    /// its last frames, so it never grows the service past its budget (docs/adr/0011-sensor-service-memory-budget.md).
    /// </summary>
    public const int LowsKept = 30_000;

    private const double Second = 1000;

    private readonly Lock _gate = new();
    private readonly Dictionary<int, Track> _tracks = [];
    private double? _closed;

    public void Add(Present present)
    {
        lock (_gate)
        {
            // Its second has closed: counted now, it would be counted in the wrong one.
            if (present.At <= _closed || present.Source == PresentSource.KernelBlit)
            {
                return;
            }

            if (!_tracks.TryGetValue(present.Process, out var track))
            {
                _tracks[present.Process] = track = new Track();
            }

            track.Parent = present.Parent;
            track.Pending.Add((present.At, RankOf(present.Source)));
        }
    }

    /// <summary>
    /// Closes the second that ends <paramref name="at"/>, on the trace's clock, and gives its rates, the busiest first; the
    /// first second is taken as one second long.
    /// </summary>
    public IReadOnlyList<FrameRate> Close(double at)
    {
        lock (_gate)
        {
            var from = _closed ?? (at - Second);
            if (at <= from)
            {
                return [];
            }

            _closed = at;
            var seconds = (at - from) / Second;
            var rates = new List<(FrameRate Rate, int Frames, Track Track)>();
            foreach (var (process, track) in _tracks.ToList())
            {
                var (frames, longest, last) = track.Take(at);
                if (last is null || at - track.Last > KeptFor)
                {
                    // A process whose first presents belong to a later second waits for it.
                    if (track.Pending.Count == 0)
                    {
                        _tracks.Remove(process);
                    }

                    continue;
                }

                var fps = frames / seconds;
                track.Steady = track.Steady >= SteadyFor ? SteadyFor : fps >= SteadyFps ? track.Steady + 1 : 0;
                if (track.Steady >= SteadyFor)
                {
                    rates.Add((new FrameRate(process, track.Parent, fps, Math.Min(Math.Max(longest, at - last.Value), LongestFrame)), frames, track));
                }
            }

            // Only the slots' lows are worked out, since each sorts up to a minute of frames.
            return
            [
                .. rates.OrderByDescending(rate => rate.Frames).ThenBy(rate => rate.Rate.Process).Take(Slots)
                    .Select(rate => rate.Track.WithLows(rate.Rate, at)),
            ];
        }
    }

    private static int RankOf(PresentSource source) =>
        source == PresentSource.Kernel ? 1
        : source == PresentSource.KernelDetailed ? 2
        : source == PresentSource.KernelBlitRect ? 3
        : 0;

    private sealed class Track
    {
        private const int Ranks = 4;

        // When the process last presented as each rank of source told.
        private readonly double?[] _lastOf = new double?[Ranks];

        // The waits between its frames as the counted source told them, and when each ended, for its lows.
        private readonly Queue<(double At, double Wait)> _waits = new();

        // The rank the last second with frames took.
        private int? _told;

        public int Parent { get; set; }

        public List<(double At, int Rank)> Pending { get; } = [];

        public double? Last => _lastOf.Max();

        // The seconds in a row it presented steadily, up to SteadyFor, from which on it counts until it stops for good.
        public int Steady { get; set; }

        /// <summary>
        /// The presents up to the second's end as the source that told of most, the longest wait between two of them, from
        /// the last the source of the second before told of, and that source's last present.
        /// </summary>
        public (int Frames, double Longest, double? Last) Take(double at)
        {
            Pending.Sort();
            var taken = Pending.TakeWhile(present => present.At <= at).ToList();
            Pending.RemoveRange(0, taken.Count);
            var counts = new int[Ranks];
            taken.ForEach(present => counts[present.Rank]++);
            var rank = taken.Count == 0 ? _told ?? 0 : Array.IndexOf(counts, counts.Max());
            var previous = _lastOf[_told ?? rank];
            var longest = 0.0;
            foreach (var (time, _) in taken.Where(present => present.Rank == rank))
            {
                var wait = time - (previous ?? time);
                longest = Math.Max(longest, wait);
                Keep(time, wait);
                previous = time;
            }

            taken.ForEach(present => _lastOf[present.Rank] = present.At);
            _told = taken.Count == 0 ? _told : rank;
            return (counts[rank], longest, _lastOf[rank] ?? previous);
        }

        /// <summary>
        /// The rate with its lows: the frames a second of its frame at the 99th and the 99.9th percentile of frame time, by
        /// nearest rank, among those that ended in the minute up to <paramref name="at"/>; none without such frames.
        /// </summary>
        public FrameRate WithLows(FrameRate rate, double at)
        {
            while (_waits.TryPeek(out var oldest) && oldest.At <= at - LowsOver)
            {
                _waits.Dequeue();
            }

            if (_waits.Count == 0)
            {
                return rate;
            }

            var sorted = ArrayPool<double>.Shared.Rent(_waits.Count);
            try
            {
                var count = 0;
                foreach (var (_, wait) in _waits)
                {
                    sorted[count++] = wait;
                }

                Array.Sort(sorted, 0, count);
                return rate with
                {
                    OnePercentLow = Second / sorted[count - (count / 100) - 1],
                    PointOnePercentLow = Second / sorted[count - (count / 1000) - 1],
                };
            }
            finally
            {
                ArrayPool<double>.Shared.Return(sorted);
            }
        }

        // A first frame has no wait before it, and two told at the same time no gap between them.
        private void Keep(double at, double wait)
        {
            if (wait <= 0)
            {
                return;
            }

            _waits.Enqueue((at, wait));
            if (_waits.Count > LowsKept)
            {
                _waits.Dequeue();
            }
        }
    }
}
