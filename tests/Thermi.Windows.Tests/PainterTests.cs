using System.Drawing;
using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Painting;

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

    // Night QA: a press 4 px in from a round floating overlay's corner fell through to the window behind, as a layered
    // window lets clicks through its transparent pixels. Its resizing corner catches the press across its whole square.
    [Theory]
    [InlineData("free", true)]
    [InlineData("locked", false)]
    [InlineData("taskbar", false)]
    public void AFloatingOverlaysCornerCatchesThePressAcrossItsSquare(string overlay, bool catches)
    {
        using var painter = new Painter();
        using var bitmap = painter.Paint(Layout(9), Look, 1);
        var config = new OverlayConfig
        {
            Position = overlay == "taskbar" ? null : new Position(100, 100),
            Locked = overlay == "locked",
        };

        Gestures.OverlayGestures.HoldCorner(bitmap, config, 1);

        Assert.Equal(catches, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A > 0);
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

    // Night QA (N1): while the window edits another overlay, this one fades its frame, and its white values on a white page
    // read as white on light grey. Over the backdrop that shows its pills least, each value keeps 3:1 against what borders it.
    [Theory]
    [InlineData(PillTheme.Dark, 255)]
    [InlineData(PillTheme.Light, 0)]
    public void AFadedFramesValuesReadOnAnyBackdrop(PillTheme pills, int backdrop)
    {
        using var painter = new Painter();
        var items = Layout(9);
        var look = new EditMarks(Fade.Frame, null, null, default).Applied(OverlayLook.Of(new StyleConfig { Pills = pills }, lightTaskbar: false));

        using var bitmap = painter.Paint(items, look, 1);

        var value = Rectangle.Round(painter.Places.Single(each => each.Id == Metrics.CpuLoad.Id).Value.Rect());
        var lights = Enumerable.Range(value.Left, value.Width)
            .SelectMany(x => Enumerable.Range(value.Top, value.Height).Select(y => Luminance(bitmap.GetPixel(x, y), backdrop)))
            .ToList();
        Assert.InRange((lights.Max() + 0.05) / (lights.Min() + 0.05), 3, double.MaxValue);
    }

    // A pixel's relative luminance once laid over a gray backdrop of that level, as the screen then shows it.
    private static double Luminance(Color pixel, int backdrop) =>
        (0.2126 * Over(pixel.R, pixel.A, backdrop)) + (0.7152 * Over(pixel.G, pixel.A, backdrop)) + (0.0722 * Over(pixel.B, pixel.A, backdrop));

    private static double Over(int channel, int alpha, int backdrop)
    {
        var shown = ((channel * alpha) + (backdrop * (255 - alpha))) / 255.0 / 255.0;
        return shown <= 0.03928 ? shown / 12.92 : Math.Pow((shown + 0.055) / 1.055, 2.4);
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
