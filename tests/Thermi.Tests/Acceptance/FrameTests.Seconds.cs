using System.Globalization;
using Thermi.Core.Frames;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

public sealed partial class FrameTests
{
    // What the overlay shows for FPS and frame time each second, from the presents the service traced, through the slots
    // its snapshot carries. A second's FPS is its presents over its length; its frame time the longest wait for a frame that
    // ended in it, or the one still going; a process that stops presenting reads 0 FPS for 5 s, then takes no room; a
    // present the trace delivers after its second closed is not counted in the next; presents delivered out of order are
    // counted in order. The kernel's presents, marked k, and its detailed ones, marked d, repeat the runtime's, so a second
    // counts the source that told of most, the runtime's on a tie. Each row comes after 3 s of
    // steady presents of its first source, so the app counts as presenting, and its times from half a millisecond after.
    [Theory] // Rows: each a rule the comment states, or the change of source a fix reproduced.
    [InlineData("0 100 200 300 400 500 600 700 800 900 1000", "1000", "11 100.0 ms")]
    [InlineData("0 100", "1000 2000 3000 4000 5000 6000 7000", "2 900.0 ms|0 999.9 ms|0 999.9 ms|0 999.9 ms|0 999.9 ms||")]
    [InlineData("100 1050 950", "1000 2000", "1 900.0 ms|1 950.0 ms")]
    [InlineData("100 900 700 1200", "1000 2000", "3 600.0 ms|1 800.0 ms")]
    [InlineData("0 k2 500 k502", "1000", "2 500.0 ms")]
    [InlineData("k0 d1 k500 d501", "1000", "2 500.0 ms")]
    public void EachSecondCountsItsFramesAndItsLongestWait(string presents, string closes, string expected)
    {
        const double After = 3000.5;
        var tally = new FrameTally();
        var sent = presents.Split(' ')
            .Select(each => (
                At: After + double.Parse(each.TrimStart('k', 'd'), CultureInfo.InvariantCulture),
                Source: each[0] switch { 'k' => PresentSource.Kernel, 'd' => PresentSource.KernelDetailed, _ => PresentSource.Dxgi }))
            .ToList();
        for (var second = 1000.0; second <= 3000; second += 1000)
        {
            for (var at = second - 1000; at < second; at += 10)
            {
                tally.Add(new Present(Game, 1, at, sent[0].Source));
            }

            tally.Close(second);
        }

        var times = sent.Select(each => each.At).ToList();
        var seconds = new List<string>();
        var delivered = 0;
        foreach (var close in closes.Split(' ').Select(each => After + double.Parse(each, CultureInfo.InvariantCulture)))
        {
            // The trace delivers presents in the order the row gives, up to the first that is past the close.
            for (; delivered < times.Count && times[..(delivered + 1)].Max() <= close; delivered++)
            {
                tally.Add(new Present(Game, 1, times[delivered], sent[delivered].Source));
            }

            var overlay = OverlayConfig.First with { Items = [.. OverlayConfig.First.Items, FrameTimeItem] };
            var shown = Shown(overlay, Desktop, tally.Close(close), Game);
            seconds.Add(string.Join(" ", new[] { Metrics.Fps.Id, Metrics.FrameTime.Id }.Select(shown.GetValueOrDefault).OfType<string>()));
        }

        Assert.Equal(expected, string.Join("|", seconds));
    }
}
