using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Painting;

namespace Thermi.Windows.Tests;

/// <summary>
/// The overlay keeps its size while the numbers change, so it never jitters on the taskbar, and draws the meters, fonts and
/// corners its look gives.
/// </summary>
public sealed class PainterTests
{
    private static readonly OverlayLook Look = OverlayLook.Of(new StyleConfig(), lightTaskbar: false);

    [Theory]
    [InlineData(2, 12)]
    [InlineData(9, 100)]
    [InlineData(double.NaN, 55)]
    public void ThePillsKeepTheirWidthWhateverTheNumbers(double before, double after)
    {
        using var painter = new Painter();

        using var first = painter.Paint(Layout(before), Look, 1);
        using var second = painter.Paint(Layout(after), Look, 1);

        Assert.Equal(first.Width, second.Width);
    }

    // A graph is 24 px wide and a meter 12, each 6 px from its value.
    [Fact]
    public void AGraphIsTwelvePixelsWiderThanAMeterAndEveryFontHasItsOwnWidth()
    {
        using var painter = new Painter();
        // The CPU load starts with its line; given bars, it draws them instead.
        var graphed = Layout(9);
        var metered = OverlayLayout.Build(
            PowerEdits.SetGraph(new ThermiConfig(), "cpu.load", GraphStyle.Bars).Config.Overlays[0],
            Readings(9),
            CultureInfo.GetCultureInfo("en-US"));

        using var withMeter = painter.Paint(metered, Look, 1);
        using var withGraph = painter.Paint(graphed, Look, 1);
        var widths = new[] { StyleFont.Segoe, StyleFont.Bahnschrift, StyleFont.Consolas }
            .Select(font => Width(painter, metered, OverlayLook.Of(new StyleConfig { Font = font }, lightTaskbar: false)))
            .ToList();

        Assert.InRange(withGraph.Width - withMeter.Width, 11, 13);
        Assert.Equal(3, widths.Distinct().Count());
    }

    [Theory]
    [InlineData(CornerStyle.Round, 0)]
    [InlineData(CornerStyle.Soft, 0)]
    [InlineData(CornerStyle.Square, 255)]
    public void OnlySquareCornersFillThePillsCorner(CornerStyle corners, int alpha)
    {
        using var painter = new Painter();

        using var bitmap = painter.Paint(Layout(9), OverlayLook.Of(new StyleConfig { Corners = corners }, lightTaskbar: false), 1);

        Assert.Equal(alpha, bitmap.GetPixel(1, 1).A);
    }

    // Cold review 4 (Q9): the open window marks the selected value with a short bar lying under it, as a tab's mark, never a
    // standing one beside its digits that reads as a text cursor.
    [Fact]
    public void TheSelectedValuesMarkLiesUnderIt()
    {
        using var painter = new Painter();
        var items = Layout(9);
        using var bitmap = painter.Paint(items, Look, 1);
        var accent = new Rgb(255, 0, 255);

        new EditMarks(Fade.None, Metrics.CpuLoad.Id, null, accent).Draw(bitmap, painter.Places, items, 1);

        var place = painter.Places.Single(each => each.Id == Metrics.CpuLoad.Id);
        var marked = Enumerable.Range(0, bitmap.Width)
            .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => (X: x, Y: y)))
            .Where(at => bitmap.GetPixel(at.X, at.Y) is { A: 255, R: 255, G: 0, B: 255 })
            .ToList();
        var (left, right, top, bottom) = (marked.Min(at => at.X), marked.Max(at => at.X), marked.Min(at => at.Y), marked.Max(at => at.Y));
        Assert.True(right - left > bottom - top, "The mark lies down.");
        Assert.InRange((left + right) / 2f, place.Value.Left, place.Value.Right);
        Assert.True(top > place.Pill.Top + (place.Pill.Height / 2), "The mark is under the value's middle.");
    }

    private static int Width(Painter painter, IReadOnlyList<OverlayItem> items, OverlayLook look)
    {
        using var bitmap = painter.Paint(items, look, 1);
        return bitmap.Width;
    }

    private static IReadOnlyList<OverlayItem> Layout(double value) =>
        OverlayLayout.Build(new OverlayConfig(), Readings(value), CultureInfo.GetCultureInfo("en-US"));

    private static Dictionary<string, Reading> Readings(double value) =>
        new(StringComparer.Ordinal)
        {
            [Metrics.CpuFan.Id] = new(SensorKind.Fan, value * 20),
            [Metrics.CpuTemperature.Id] = new(SensorKind.Temperature, value),
            [Metrics.CpuLoad.Id] = new(SensorKind.Load, value),
            [Metrics.CpuPower.Id] = new(SensorKind.Power, value * 3),
            [Metrics.GpuLoad.Id] = new(SensorKind.Load, value),
            [Metrics.RamUsed.Id] = new(SensorKind.Data, value / 3),
        };
}
