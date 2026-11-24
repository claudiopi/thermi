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

    [Fact]
    public void AGraphMeterIsThirtyPixelsWideAndEveryFontHasItsOwnWidth()
    {
        using var painter = new Painter();
        var plain = Layout(9);
        var graphed = OverlayLayout.Build(
            PowerEdits.SetGraph(new ThermiConfig(), "cpu.load", graph: true).Config.Overlays[0],
            Readings(9),
            CultureInfo.GetCultureInfo("en-US"));

        using var withoutGraph = painter.Paint(plain, Look, 1);
        using var withGraph = painter.Paint(graphed, Look, 1);
        var widths = new[] { StyleFont.Segoe, StyleFont.Bahnschrift, StyleFont.Consolas }
            .Select(font => Width(painter, plain, OverlayLook.Of(new StyleConfig { Font = font }, lightTaskbar: false)))
            .ToList();

        Assert.InRange(withGraph.Width - withoutGraph.Width, 38, 40);
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
