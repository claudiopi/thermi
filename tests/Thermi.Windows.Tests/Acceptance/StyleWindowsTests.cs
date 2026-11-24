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

    // The recorded CPU load, replayed as `thermi preview` replays a series: the CPU load's graph, which has no lines, draws
    // every second it read in the normal color, unbroken by the track between columns, at a scale where a second is a
    // pixel, on dark and on light pills.
    [Fact]
    public void AGraphDrawsEachSecondInItsLevelsColor()
    {
        const float scale = 2;
        var series = TestRecordings.Series(TestRecordings.CpuLoad);
        using var painter = new Painter();

        var lit = new[] { PillTheme.Dark, PillTheme.Light }.Select(pills =>
        {
            var config = StyleEdits.Restyle(new ThermiConfig(), 0, style => style with { Pills = pills }).Config;
            var look = OverlayLook.Of(config.Overlays[0].Style, lightTaskbar: false);
            using var bitmap = painter.Paint(Preview.Shown(config, series, baseClock: null), look, scale);
            var load = painter.Places.Single(place => place.Id == Metrics.CpuLoad.Id);
            var normal = PainterShapes.Drawn(look.Normal).ToArgb();
            return Enumerable.Range((int)load.Item.Left, (int)(load.Value.Left - load.Item.Left))
                .Count(x => Enumerable.Range(0, bitmap.Height).Any(y => bitmap.GetPixel(x, y).ToArgb() == normal));
        });

        Assert.All(lit, columns => Assert.InRange(columns, series.Count - 1, series.Count));
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
