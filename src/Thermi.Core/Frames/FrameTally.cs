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
    /// The kernel's copy of a frame into its window, the only event that tells of a game's frames while a capture, as OBS's,
    /// redirects them.
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
/// still going at the second's end included, in milliseconds.
/// </summary>
public readonly record struct FrameRate(int Process, int Parent, double Fps, double FrameTime);

/// <summary>
/// Turns presents into each second's frame rates, as the sensor service reads them (docs/adr/0014-frames-from-etw.md).
/// Presents come from the trace's thread and seconds close on the reading's, so both take the same lock. A trace delivers
/// presents late and not quite in order, so each is kept until the second that holds it closes. A runtime's present
/// reaches the kernel too, and may be told by both of its histories, so each source counts only for a process that sent
/// none of an earlier one in the last 5 s: the runtimes', then the kernel's plain history, then its detailed one, as for
/// Vulkan and OpenGL drivers that present on their own, then its copies into a window, as while OBS captures a game.
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
            var rank = RankOf(present.Source);
            track.Seen[rank] = Math.Max(track.Seen[rank] ?? present.At, present.At);
            if (track.Seen.Take(rank).Any(seen => seen is { } at && present.At - at <= KeptFor))
            {
                return;
            }

            track.Pending.Add(present.At);
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
            var rates = new List<(FrameRate Rate, int Frames)>();
            foreach (var (process, track) in _tracks.ToList())
            {
                var (frames, longest) = track.Take(at);
                if (track.Last is not { } last || at - last > KeptFor)
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
                    rates.Add((new FrameRate(process, track.Parent, fps, Math.Min(Math.Max(longest, at - last), LongestFrame)), frames));
                }
            }

            return [.. rates.OrderByDescending(rate => rate.Frames).ThenBy(rate => rate.Rate.Process).Take(Slots).Select(rate => rate.Rate)];
        }
    }

    private static int RankOf(PresentSource source) =>
        source == PresentSource.Kernel ? 1
        : source == PresentSource.KernelDetailed ? 2
        : source == PresentSource.KernelBlitRect ? 3
        : 0;

    private sealed class Track
    {
        public int Parent { get; set; }

        // When the process last presented as told by each rank of source, which a later rank's would repeat.
        public double?[] Seen { get; } = new double?[4];

        public List<double> Pending { get; } = [];

        public double? Last { get; private set; }

        // The seconds in a row it presented steadily, up to SteadyFor, from which on it counts until it stops for good.
        public int Steady { get; set; }

        // The presents up to the second's end, in order, and the longest wait between two of them.
        public (int Frames, double Longest) Take(double at)
        {
            Pending.Sort();
            var taken = 0;
            var longest = 0.0;
            for (; taken < Pending.Count && Pending[taken] <= at; taken++)
            {
                var time = Pending[taken];
                if (Last is { } last)
                {
                    longest = Math.Max(longest, time - last);
                }

                Last = time;
            }

            Pending.RemoveRange(0, taken);
            return (taken, longest);
        }
    }
}
