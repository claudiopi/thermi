using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What every OS's painter lays out alike from Core, whatever its text measures: a graph's runs, a single bar's pills, and
/// what a press or a drop at a point of the picture is on.
/// </summary>
public sealed class PaintedShapesTests
{
    private static readonly OverlayLook Look = OverlayLook.Of(new StyleConfig(), lightTaskbar: false);

    // Seconds as n normal, w warning, c critical and . missing; a run as its level, its seconds, and + when its line runs on
    // to the next second's height.
    [Theory] // Rows: levels that change, a missing second that breaks the run, and none
    [InlineData("nnwc", "n2+ w1+ c1")]
    [InlineData("nn.n", "n2 n1")]
    [InlineData("", "")]
    public void AGraphIsARunForEachLevelBrokenByAMissingSecond(string seconds, string expected)
    {
        GraphPoint?[] points = [.. seconds.Select(second => second switch
        {
            '.' => (GraphPoint?)null,
            'w' => new GraphPoint(0.5, Level.Warning),
            'c' => new GraphPoint(1, Level.Critical),
            _ => new GraphPoint(0.25, Level.Normal),
        })];

        var (track, runs) = MeterShapes.Graph(points, 0, 20, Look, 1);

        Assert.True(track.Width > 0);
        Assert.Equal(expected, string.Join(' ', runs.Select(run =>
        {
            var count = (run.Fill.Count - 2) / 2;
            return string.Create(CultureInfo.InvariantCulture, $"{run.Level.ToString()[0]}{count}{(run.Line.Count > count * 2 ? "+" : "")}")
                .ToLowerInvariant();
        })));
    }

    // Two items in a pill from 0 to 100, a and b, b's value alone from 60, a's label from 0 to 20, and c in a pill from 110 to
    // 160: what a press finds, as its item, l when on its pill's label, and where a drop there joins, - for after the last.
    [Theory] // Rows: on a label, past it, the edge, a drop past a pill's middle, past the last, along a standing overlay, within its own pill
    [InlineData(10, 10, null, false, "a l > a")]
    [InlineData(70, 10, null, false, "b > c")]
    [InlineData(105, 10, null, false, "- > c")]
    [InlineData(150, 10, null, false, "c > -")]
    [InlineData(150, 2, null, true, "c > a")]
    [InlineData(80, 10, "b", false, "b > - in")]
    [InlineData(10, 10, "b", false, "a l > a in")]
    public void APressAndADropFindWhatIsThere(double x, double y, string? alone, bool vertical, string expected)
    {
        var first = new PillBox(0, 0, 100, 20);
        PaintedItem[] places =
        [
            new("a", new PillBox(0, 0, 50, 20), new PillBox(25, 0, 25, 20)) { Pill = first, Label = new PillBox(0, 0, 20, 20) },
            new("b", new PillBox(50, 0, 50, 20), new PillBox(60, 0, 40, 20)) { Pill = first, Alone = new PillBox(60, 0, 40, 20) },
            new("c", new PillBox(110, 0, 50, 20), new PillBox(120, 0, 40, 20)) { Pill = new PillBox(110, 0, 50, 20) },
            new("", new PillBox(100, 0, 10, 20), default),
        ];

        var pressed = places.Pressed(x, y);
        var drop = places.JoinAt(0, x, y, alone, vertical);

        Assert.Equal(
            expected,
            $"{pressed.Item ?? "-"}{(pressed.OnLabel ? " l" : "")} > {drop.Before ?? "-"}{(drop.InPill ? " in" : "")}");
    }

    // Three items 30, 20 and 40 wide at 100 %: one pill, or a single bar of a pill of one and a pill of two, side by side or
    // standing; as its width x its height for rows 30 high, then each pill inside it, from a box of that size.
    [Theory] // Rows: no bar, a bar lying, a bar standing
    [InlineData(null, false, "146x30: 0,0,146,30")]
    [InlineData(new[] { 1, 2 }, false, "156x30: 4,4,50,22 58,4,94,22")]
    [InlineData(new[] { 1, 2 }, true, "102x56: 4,4,94,22 4,30,94,22")]
    public void ASingleBarHoldsItsPillsSideBySideOrStanding(int[]? runs, bool stacked, string expected)
    {
        var item = new OverlayItem(null, "1", Level.Normal, "1");
        var pill = new PaintedPill([item, item, item], [30, 20, 40], 1) { Runs = runs, Stacked = stacked };

        var (width, height) = (pill.Width, pill.HeightOf(30));
        var parts = pill.PartsIn(new PillBox(0, 0, width, height));

        Assert.Equal(
            expected,
            string.Create(CultureInfo.InvariantCulture, $"{width}x{height}: ")
                + string.Join(' ', parts.Select(part => part.Box).Select(box => string.Create(
                    CultureInfo.InvariantCulture, $"{box.Left},{box.Top},{box.Width},{box.Height}"))));
    }
}
