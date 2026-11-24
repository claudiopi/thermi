using System.Drawing;
using System.Globalization;
using Thermi.Commands;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Painting;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// Pills and style are data in Core, so the stage and the overlay always agree.
/// </summary>
public sealed class StyleWindowsTests
{
    // Which opacity the file takes is Core's, in FileFormatTests; here, the lowest it takes still paints.
    [Fact]
    public void TheWholeOverlayFadesButNeverVanishes()
    {
        using var painter = new Painter();

        var painted = new[] { 0.2, 1 }
            .Select(opacity => StyleEdits.SetOpacity(new ThermiConfig(), 0, new OpacityConfig(Overlay: opacity)).Config.Overlays[0])
            .Select(overlay => OpaquestPixel(painter, overlay));

        Assert.Equal([51, 255], painted);
    }

    // The recorded CPU load, replayed as `thermi preview` replays a series: the CPU load's graph, which has no lines, is
    // 24 px wide at 100 % and draws the 45 seconds it read as a line in the normal color over a light fill of it, never a
    // block, at 100 % and 150 %, on dark and on light pills.
    [Theory]
    [InlineData(1f, PillTheme.Dark)]
    [InlineData(1.5f, PillTheme.Light)]
    public void AGraphDrawsItsSecondsAsALineOverALightFill(float scale, PillTheme pills)
    {
        var series = TestRecordings.Series(TestRecordings.CpuLoad);
        var config = StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Pills = pills }).Config;
        var look = OverlayLook.Of(config.Overlays[0].Style, lightTaskbar: false);
        using var painter = new Painter();

        using var bitmap = painter.Paint(Preview.Shown(config, series, baseClock: null), look, scale);

        var load = painter.Places.Single(place => place.Id == Metrics.CpuLoad.Id);
        var strip = Before(bitmap, load, 30 * scale);
        var track = strip.Where(pixel => Near(pixel.Color, look.Pills.MeterOff, 0)).ToList();
        var graph = strip.Where(pixel => pixel.X >= track.Min(each => each.X) && pixel.X <= track.Max(each => each.X)).ToList();
        var line = graph.Where(pixel => Near(pixel.Color, look.Normal, 48)).ToList();
        var fill = graph
            .Where(pixel => !Near(pixel.Color, look.Normal, 48) && !Near(pixel.Color, look.Pills.MeterOff, 0) && !Near(pixel.Color, look.Pills.Outer, 0))
            .ToList();
        var width = (int)Math.Round(24 * scale);
        var seconds = (int)Math.Round(series.Count * width / 60.0);
        Assert.Equal(width, track.Max(pixel => pixel.X) - track.Min(pixel => pixel.X) + 1);
        Assert.InRange(line.Select(pixel => pixel.X).Distinct().Count(), seconds - 2, seconds + 2);
        Assert.True(fill.Count > line.Count, "More of the graph is its light fill than its line.");
    }

    // The admin recording's CPU temperature, 77 °C, metered by itself: a meter of five segments 12 px wide at 100 %, the
    // four lowest lit, as 77 of 100 rounds to four fifths, in its level's color, and the top one a faint track; at 100 %
    // on the dark pills of a dark taskbar, and at 150 % past a warning line on the light pills of a light one, where the
    // meter shows the warning, so the value has no underline too.
    [Theory]
    [InlineData(1f, false, null)]
    [InlineData(1.5f, true, 60d)]
    public void AMeterLightsItsSegmentsFromTheBottomInItsLevelsColor(float scale, bool lightTaskbar, double? warning)
    {
        var config = new ThermiConfig();
        config = warning is { } line ? ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, line, 95).Config : config;
        var overlay = config.Overlays[0];
        var look = OverlayLook.Of(overlay.Style, lightTaskbar);
        var readings = SensorMap.Read(TestRecordings.Read(TestRecordings.Admin), overlay);
        using var painter = new Painter();

        using var bitmap = painter.Paint(OverlayLayout.Build(overlay, readings, CultureInfo.GetCultureInfo("en-US")), look, scale);

        var temperature = painter.Places.Single(place => place.Id == Metrics.CpuTemperature.Id);
        var meter = Before(bitmap, temperature, 20 * scale);
        var lit = meter.Where(pixel => Near(pixel.Color, look.MeterColor(warning is null ? Level.Normal : Level.Warning), 0)).ToList();
        var track = meter.Where(pixel => Near(pixel.Color, look.Pills.MeterOff, 0)).ToList();
        Assert.Equal((int)Math.Round(12 * scale), lit.Max(pixel => pixel.X) - lit.Min(pixel => pixel.X) + 1);
        Assert.Equal(4, Bands(lit));
        Assert.Equal(1, Bands(track));
        Assert.True(track.Max(pixel => pixel.Y) < lit.Min(pixel => pixel.Y), "The lit segments are the lowest.");
        var value = Enumerable.Range((int)temperature.Value.Left, (int)temperature.Value.Width)
            .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)));
        Assert.DoesNotContain(value, pixel => Near(pixel, look.Warning, 0));
    }

    // The pixels of a strip of the given width that ends where the item's value starts.
    private static List<(int X, int Y, Color Color)> Before(Bitmap bitmap, PaintedItem item, float width) =>
        [.. Enumerable.Range((int)Math.Floor(item.Value.Left - width), (int)Math.Ceiling(width))
            .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => (x, y, bitmap.GetPixel(x, y))))];

    private static bool Near(Color pixel, Rgb color, int by) =>
        Math.Abs(pixel.R - color.R) <= by && Math.Abs(pixel.G - color.G) <= by && Math.Abs(pixel.B - color.B) <= by;

    // How many runs of rows the pixels make, which are the segments they light.
    private static int Bands(List<(int X, int Y, Color Color)> pixels)
    {
        var rows = pixels.Select(pixel => pixel.Y).Distinct().Order().ToList();
        return rows.Count(row => !rows.Contains(row - 1));
    }

    private static int OpaquestPixel(Painter painter, OverlayConfig overlay)
    {
        var items = OverlayLayout.Build(overlay, new Dictionary<string, Reading>(StringComparer.Ordinal), CultureInfo.GetCultureInfo("en-US"));
        using var bitmap = painter.Paint(items, OverlayLook.Of(overlay.Style, lightTaskbar: false), 1);
        var opaquest = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                opaquest = Math.Max(opaquest, bitmap.GetPixel(x, y).A);
            }
        }

        return opaquest;
    }
}
