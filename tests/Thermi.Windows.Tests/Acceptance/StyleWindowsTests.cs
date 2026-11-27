using System.Drawing;
using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Commands;
using Thermi.Windows.Painting;
using Thermi.Windows.Win32;

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

    // An agent's picture (thermi-cli preview) is the overlay asked, at its size times the scale, as a PNG of the size its
    // measure said; laid on the taskbar's color, or high contrast's, as a person sees it there, since a client shows it on a
    // backdrop of its own.
    [Fact]
    public void AnAgentsPictureIsTheOverlayOnTheTaskbar()
    {
        var two = OverlayEdits.Add(new ThermiConfig()).Config;
        var config = two with { Overlays = [two.Overlays[0], two.Overlays[1] with { Size = 2 }] };
        var snapshot = TestRecordings.Read(TestRecordings.Admin);

        var picture = Preview.Png(config, 1, snapshot, 1.5f);

        using var png = new MemoryStream(picture.Png);
        using var decoded = new Bitmap(png);
        using var painter = new Painter();
        var light = Desktop.LightTaskbar();
        var look = OverlayLook.Of(config.Overlays[1].Style, light, Desktop.HighContrast());
        var size = painter.Measure(Preview.Shown(config, [snapshot], CpuWatch.ReadBaseClock(), overlay: 1), look, 3);
        Assert.Equal(
            (size.Width, size.Height, size.Width, size.Height, (size.Width, size.Height)),
            (picture.Width, picture.Height, decoded.Width, decoded.Height, Preview.Measure(config, 1, snapshot, 1.5f)));
        Assert.Equal(PainterShapes.Drawn(Desktop.Backdrop(light)).ToArgb(), decoded.GetPixel(0, 0).ToArgb());
    }

    // The admin recording's CPU temperature, 77 °C, metered by itself: a meter of five segments 12 px wide at 100 %, the
    // four lowest lit, as 77 of 100 rounds to four fifths, in its level's color, and the top one a faint track; at 100 %
    // on the dark pills of a dark taskbar, and at 150 % past a warning line on the light pills of a light one, where the
    // meter shows the warning, so the value has no underline too. With 36 px values, the meter and the CPU load's graph
    // keep the shape they have beside Standard's 13.5 px: as tall as the value's text, and as much wider.
    [Theory]
    [InlineData(1f, false, null, 13.5)]
    [InlineData(1.5f, true, 60d, 36)]
    public void AMeterLightsItsSegmentsFromTheBottomInItsLevelsColor(float scale, bool lightTaskbar, double? warning, double text)
    {
        var config = new ThermiConfig();
        config = warning is { } line ? ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, line, 95).Config : config;
        config = StyleEdits.SetText(config, 0, TextPart.Value, new TextStyle(text, TextWeight.Bold)).Config;
        var overlay = config.Overlays[0];
        var look = OverlayLook.Of(overlay.Style, lightTaskbar);
        var readings = SensorMap.Read(TestRecordings.Read(TestRecordings.Admin), overlay);
        using var painter = new Painter();

        using var bitmap = painter.Paint(OverlayLayout.Build(overlay, readings, CultureInfo.GetCultureInfo("en-US")), look, scale);

        var wider = text / 13.5;
        var temperature = painter.Places.Single(place => place.Id == Metrics.CpuTemperature.Id);
        var meter = Before(bitmap, temperature, (float)(20 * wider * scale));
        var lit = meter.Where(pixel => Near(pixel.Color, look.MeterColor(warning is null ? Level.Normal : Level.Warning), 0)).ToList();
        var track = meter.Where(pixel => Near(pixel.Color, look.Pills.MeterOff, 0)).ToList();
        var load = Before(bitmap, painter.Places.Single(place => place.Id == Metrics.CpuLoad.Id), (float)(30 * wider * scale))
            .Where(pixel => Near(pixel.Color, look.Pills.MeterOff, 0))
            .ToList();
        Assert.Equal((int)Math.Round(12 * wider * scale), lit.Max(pixel => pixel.X) - lit.Min(pixel => pixel.X) + 1);
        Assert.Equal((int)Math.Round(24 * wider * scale), load.Max(pixel => pixel.X) - load.Min(pixel => pixel.X) + 1);
        Assert.InRange(lit.Max(pixel => pixel.Y) - track.Min(pixel => pixel.Y) + 1 - (text * scale), -5, 5);
        Assert.Equal(4, Bands(lit));
        Assert.Equal(1, Bands(track));
        Assert.True(track.Max(pixel => pixel.Y) < lit.Min(pixel => pixel.Y), "The lit segments are the lowest.");
        var value = Enumerable.Range((int)temperature.Value.Left, (int)temperature.Value.Width)
            .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)));
        Assert.DoesNotContain(value, pixel => Near(pixel, look.Warning, 0));
    }

    // In one bar, each run, CPU, GPU and RAM, is a pill of its own inside it, in the inner color, 4 px inside the bar's
    // edge and 4 px apart at 100 %, and an item's pill is its run's; at 100 % on dark pills and at 150 % on light ones.
    // Vertical, the bar stands and its pills stack from top to bottom, all as wide as the widest.
    [Theory]
    [InlineData(1f, PillTheme.Dark, Orientation.Horizontal)]
    [InlineData(1.5f, PillTheme.Light, Orientation.Horizontal)]
    [InlineData(1.5f, PillTheme.Dark, Orientation.Vertical)]
    public void OneBarHoldsAPillPerRun(float scale, PillTheme pills, Orientation orientation)
    {
        var overlay = StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Pills = pills, Layout = PillLayout.One, Orientation = orientation })
            .Config.Overlays[0];
        var look = OverlayLook.Of(overlay.Style, lightTaskbar: false);
        var readings = SensorMap.Read(TestRecordings.Read(TestRecordings.Admin), overlay);
        using var painter = new Painter();

        using var bitmap = painter.Paint(OverlayLayout.Build(overlay, readings, CultureInfo.GetCultureInfo("en-US")), look, scale);

        var vertical = orientation == Orientation.Vertical;
        var inner = painter.Places.Select(place => place.Pill).Distinct().ToList();
        var across = (int)Math.Round(8 * scale);
        var line = Enumerable.Range(0, vertical ? bitmap.Height : bitmap.Width)
            .Where(at => Near(vertical ? bitmap.GetPixel(across, at) : bitmap.GetPixel(at, across), look.Pills.Inner, 0))
            .ToList();
        Assert.Equal(3, inner.Count);
        Assert.Equal(3, line.Count(at => !line.Contains(at - 1)));
        Assert.All(inner, box => Assert.Equal(4 * scale, vertical ? box.Left : box.Top, 0.01));
        Assert.Equal(4 * scale, vertical ? inner[0].Top : inner[0].Left, 0.01);
        Assert.All(
            inner.Zip(inner.Skip(1)),
            pair => Assert.Equal(4 * scale, vertical ? pair.Second.Top - pair.First.Bottom : pair.Second.Left - pair.First.Right, 0.01));
        Assert.True(!vertical || inner.Select(box => box.Width).Distinct().Count() == 1, "a standing bar's pills are as wide as the widest");
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
