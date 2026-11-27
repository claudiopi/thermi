using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

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

    // CPU 1, GPU 2 and an unlabeled 3, each character 10 wide at 100 %: a pill for each label, or a single bar of a pill of
    // one and a pill of two, side by side or standing; as the picture's size, then each pill in it and where each value draws.
    [Theory] // Rows: no bar, a bar lying, a bar standing
    [InlineData(PillLayout.Pills, Orientation.Horizontal, "198x38: 0,0,88,38 [54] 96,0,102,38 [150 174]")]
    [InlineData(PillLayout.One, Orientation.Horizontal, "186x38: 4,4,80,30 [54] 88,4,94,30 [138 162]")]
    [InlineData(PillLayout.One, Orientation.Vertical, "102x72: 4,4,94,30 [54] 4,38,94,30 [54 78]")]
    public void ASingleBarHoldsItsPillsSideBySideOrStanding(PillLayout layout, Orientation orientation, string expected)
    {
        var look = OverlayLook.Of(new StyleConfig { Layout = layout, Orientation = orientation }, lightTaskbar: false);
        OverlayItem[] items = [new("CPU", "1", Level.Normal, "10"), new("GPU", "2", Level.Normal, "2"), new(null, "3", Level.Warning, "3")];

        var pills = PaintLayout.PillsOf(items, look, 1, TenEach);
        var (wide, high, arranged) = PaintLayout.SizeOf(pills, look, 1);
        var parts = pills.SelectMany((pill, p) => pill.PartsIn(arranged.Pills[p]).Select(part => (Pill: p, Part: part)));

        Assert.Equal(expected, string.Create(CultureInfo.InvariantCulture, $"{wide}x{high}:") + string.Concat(parts.Select(each =>
            string.Create(CultureInfo.InvariantCulture, $" {Shown(each.Part.Box)} [")
                + string.Join(' ', PaintLayout.ItemsIn(pills[each.Pill], each.Part, arranged.Pills[each.Pill], look, 1, TenEach)
                    .Select(drawn => string.Create(CultureInfo.InvariantCulture, $"{drawn.ValueX:0.#}")))
                + "]")));
    }

    private static double TenEach(string text, TextRole role) => text.Length * 10;

    private static string Shown(PillBox box) =>
        string.Create(CultureInfo.InvariantCulture, $"{box.Left:0.#},{box.Top:0.#},{box.Width:0.#},{box.Height:0.#}");

    // thermi's mark at a tray icon's side in pixels, as every painter fills its cells: the body's cells, its eyes', its
    // leftmost and rightmost colors, or - for the ink of a mark with none; past 31 px it is drawn, with no cells.
    [Theory] // Rows: each eye at each hand-placed size, a three-color gradient, a mark of the ink alone, and one drawn
    [InlineData(16, MarkState.Calm, "16: 92 body, 4 eyes, #405BFF to #824EFF")]
    [InlineData(24, MarkState.Hot, "24: 180 body, 32 eyes, #FF761B to #FF422D")]
    [InlineData(20, MarkState.App, "16: 88 body, 8 eyes, #415AFF to #E26F53")]
    [InlineData(28, MarkState.Stopped, "24: 188 body, 0 eyes, - to -")]
    [InlineData(32, MarkState.Warm, "drawn")]
    public void TheMarkIsHandPlacedAtATrayIconsSizes(int side, MarkState state, string expected)
    {
        var look = MarkLook.Of(state);
        var said = MarkPixels.For(side) is not { } pixels ? "drawn" : Said(pixels, [.. pixels.Body(look)]);

        Assert.Equal(expected, said);

        static string Said(MarkPixels pixels, List<MarkCell> cells)
        {
            var body = cells.Where(cell => cell.Color != MarkLook.EyeInk).OrderBy(cell => cell.X).ToList();
            var (left, right) = (body[0].Color?.ToString() ?? "-", body[^1].Color?.ToString() ?? "-");
            return string.Create(CultureInfo.InvariantCulture, $"{pixels.Size}: {body.Count} body, {cells.Count - body.Count} eyes, {left} to {right}");
        }
    }

    // A click passes through a rounded pill's corners, where nothing is drawn: it takes clicks in bands inside its curve.
    [Theory] // Rows: square, round, and too narrow for its round ends' bands
    [InlineData(100, 0, "0,0,100,24")]
    [InlineData(100, 12, "12,0,76,4 3.1,4,93.9,4 0.7,8,98.6,4 0.7,12,98.6,4 3.1,16,93.9,4 12,20,76,4")]
    [InlineData(20, 12, "3.1,4,13.9,4 0.7,8,18.6,4 0.7,12,18.6,4 3.1,16,13.9,4")]
    public void APillTakesClicksOnlyWhereItIsDrawn(double width, double radius, string expected) =>
        Assert.Equal(
            expected,
            string.Join(' ', PaintLayout.Taking(new PillBox(0, 0, width, 24), radius).Select(box => string.Create(
                CultureInfo.InvariantCulture, $"{box.Left:0.#},{box.Top:0.#},{box.Width:0.#},{box.Height:0.#}"))));
}
