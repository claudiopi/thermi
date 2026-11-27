using Thermi.Core.Frames;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

// The game's 1 % and 0.1 % lows, its slowest frames of the last minute, and the replay every test of frames shares.
public sealed partial class FrameTests
{
    private static readonly string[] LowSensors = [GameSensors.OnePercentLowId, GameSensors.PointOnePercentLowId];

    private static readonly string[] LowIds = [Metrics.OnePercentLow.Id, Metrics.PointOnePercentLow.Id];

    // What the overlay shows for frames: FPS and frame time, then the 1 % and 0.1 % lows.
    private static readonly string[] FramesAndLows = [Metrics.Fps.Id, Metrics.FrameTime.Id, .. LowIds];

    private static ItemConfig[] LowItems =>
    [
        new() { Id = Metrics.OnePercentLow.Id, Metric = Metrics.OnePercentLow.Id },
        new() { Id = Metrics.PointOnePercentLow.Id, Metric = Metrics.PointOnePercentLow.Id },
    ];

    // Games made up frame by frame, as FPS and its lows show each second. At 100 FPS and at 1000, for its first 30 s every
    // 50th frame takes twice as long and every 500th ten times, so 2 % of its frames are slow and 0.2 % slower, then it is
    // steady for 70 s: the 1 % low reads the slow frames' rate and the 0.1 % low the slower ones', until they are a minute
    // old, at 75 s the 1 % low steady again and the 0.1 % low reading the slow ones left. At 1000 FPS a process keeps its
    // last 30 000 frames alone, so by 65 s, though not a minute old, both are steady. At 10 FPS with a 3 s load at 5 s, the
    // lows hold while FPS reads 0, the load counts once it ends, at 8 s, as one frame of 999.9 ms, as frame time caps it,
    // until 60 s later; and after a 6 s pause from 75 s, which drops it, its minute starts again.
    [Fact]
    public void TheLowsReadTheSlowestFramesOfTheLastMinute()
    {
        static string[] Seconds(IReadOnlyList<Present> presents, params int[] seconds)
        {
            var replayed = Replay(presents).ToList();
            var overlay = OverlayConfig.First with { Items = [.. OverlayConfig.First.Items, .. LowItems] };
            return
            [
                .. seconds.Select(second => Shown(overlay, Desktop, replayed[second - 1], Game))
                    .Select(shown => string.Join(" ", FramesAndLows.Select(shown.GetValueOrDefault).OfType<string>())),
            ];
        }

        static List<Present> Stuttering(double wait)
        {
            var presents = new List<Present>();
            var (at, frame) = (0.0, 0);
            while (at < 100_000)
            {
                frame++;
                at += at >= 30_000 || frame % 50 != 0 ? wait : frame % 500 == 0 ? 10 * wait : 2 * wait;
                presents.Add(new Present(Game, 1, at));
            }

            return presents;
        }

        static IEnumerable<Present> Every(double wait, double after, double until)
        {
            return Enumerable.Range(1, (int)((until - after) / wait)).Select(frame => new Present(Game, 1, after + (frame * wait)));
        }

        Assert.Equal(["98 50 1% 10 0.1%", "100 100 1% 50 0.1%", "100 100 1% 100 0.1%"], Seconds(Stuttering(10), 20, 75, 95));
        Assert.Equal(["964 500 1% 100 0.1%", "1000 1000 1% 1000 0.1%"], Seconds(Stuttering(1), 20, 65));
        Assert.Equal(
            ["0 10 1% 10 0.1%", "10 1 1% 1 0.1%", "10 10 1% 1 0.1%", "10 10 1% 10 0.1%", "10 10 1% 10 0.1%"],
            Seconds([.. Every(100, 0, 5000), .. Every(100, 7900, 75_000), .. Every(100, 80_950, 100_000)], 7, 9, 67, 68, 84));
    }

    // The presents a second at a time, through the tally the service runs, as the slots each second's snapshot carries.
    private static IEnumerable<IReadOnlyList<FrameRate>> Replay(IReadOnlyList<Present> presents)
    {
        var tally = new FrameTally();
        var end = presents.Max(present => present.At);
        for (var close = 1000.0; close <= end + FrameTally.KeptFor; close += 1000)
        {
            foreach (var present in presents.Where(present => present.At > close - 1000 && present.At <= close))
            {
                tally.Add(present);
            }

            yield return tally.Close(close);
        }
    }
}
