using System.Globalization;
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
