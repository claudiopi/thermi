using Thermi.Core.Frames;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

// The game's 1 % and 0.1 % lows, its slowest frames of the last minute, and the replay every test of frames shares.
public sealed partial class FrameTests
{
    private const string LowSensor = "/game/0/framerate/1";

    private static readonly string[] LowIds = [Metrics.OnePercentLow.Id, Metrics.PointOnePercentLow.Id];

    // What the overlay shows for frames: FPS and frame time, then the 1 % and 0.1 % lows.
    private static readonly string[] FramesAndLows = [Metrics.Fps.Id, Metrics.FrameTime.Id, .. LowIds];

    private static ItemConfig[] LowItems =>
    [
        new() { Id = Metrics.OnePercentLow.Id, Metric = Metrics.OnePercentLow.Id },
        new() { Id = Metrics.PointOnePercentLow.Id, Metric = Metrics.PointOnePercentLow.Id },
    ];

    // A game made up frame by frame, at 100 FPS and at 1000: for its first 30 s every 50th frame takes twice as long and
    // every 500th ten times, so 2 % of its frames are slow and 0.2 % slower, then it is steady for 70 s. Its 1 % low reads
    // the slow frames' rate and its 0.1 % low the slower ones', until they are a minute old: at 75 s the 1 % low is steady
    // again and the 0.1 % low reads the slow ones left. At 1000 FPS a process keeps its last 30 000 frames alone, so by
    // 65 s, though they are not a minute old, both are steady.
    [Fact]
    public void TheLowsReadTheSlowestFramesOfTheLastMinute()
    {
        static string[] Lows(double wait, params int[] seconds)
        {
            var presents = new List<Present>();
            var (at, frame) = (0.0, 0);
            while (at < 100_000)
            {
                frame++;
                at += at >= 30_000 || frame % 50 != 0 ? wait : frame % 500 == 0 ? 10 * wait : 2 * wait;
                presents.Add(new Present(Game, 1, at));
            }

            var replayed = Replay(presents).ToList();
            var lows = OverlayConfig.First with { Items = [.. LowItems] };
            return
            [
                .. seconds.Select(second => Shown(lows, Desktop, replayed[second - 1], Game))
                    .Select(shown => string.Join(" ", LowIds.Select(shown.GetValueOrDefault))),
            ];
        }

        Assert.Equal(["50 10", "100 50", "100 100"], Lows(10, 20, 75, 95));
        Assert.Equal(["500 100", "1000 1000"], Lows(1, 20, 65));
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
