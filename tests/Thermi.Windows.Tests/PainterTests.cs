using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
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

    // QA r4 (R4-02): the default six items fit their taskbar's row as soon as it has the room they are drawn in, each value
    // already at its widest form, and not a pixel before.
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void AnOverlayFitsItsTaskbarWhenTheWidthItIsDrawnAtDoes(int missing, bool crowded)
    {
        using var painter = new Painter();
        using var apps = new TaskbarApps(new FakeTimeProvider(), run => run(), () => { }, reads: false);
        var config = new ThermiConfig();
        var snapshot = TestRecordings.Read(TestRecordings.Admin);
        var items = OverlayLayout.Build(
            config.Overlays[0],
            SensorMap.Read(snapshot, config.Overlays[0]),
            CultureInfo.CurrentCulture,
            Texts.For(config.Language, CultureInfo.CurrentUICulture),
            config.Temperature);
        using var drawn = painter.Paint(items, OverlayLook.Of(config.Overlays[0].Style, Desktop.LightTaskbar()), 1.5f);
        var room = 12 + drawn.Width + 12 - missing;
        var screen = new Screen("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1368), 1.5)
        {
            Taskbar = new PixelRect(0, 1368, 2560, 1440),
            Tray = new PixelRect(room, 1368, 2560, 1440),
        };

        Assert.Equal(crowded, TaskbarRoom.Crowded(painter, apps, config, snapshot, [screen]).Count > 0);
        // The map marks the spot as having no room, from the same width.
        Assert.Equal(crowded, TaskbarRoom.Roomless(painter, apps, config, 0, snapshot, [screen]).Contains(new SnapConfig(SnapSpot.TaskbarRight)));
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
