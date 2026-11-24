using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Thermi.Painting;

namespace Thermi.Ui.Views;

/// <summary>
/// Where each overlay shows, worked out as its own window places it: the items it shows, measured as its painter keeps
/// their room at its monitor's scale, at the place its snap or position gives that size. Worked out from the settings
/// the window holds, it never waits for the overlay to move.
/// </summary>
internal static class OverlayPlaces
{
    public static IReadOnlyList<Rect?> Of(ThermiConfig config, TrayInput input, IReadOnlyList<Screen> screens)
    {
        using var painter = new Painter();
        var places = new List<Rect?>(config.Overlays.Count);
        for (var at = 0; at < config.Overlays.Count; at++)
        {
            var overlay = config.Overlays[at];
            var shown = ThermiMark.Overlay(config, at, input, windowOpenedOnce: true);
            var look = OverlayLook.Of(overlay.Style, input.LightTaskbar);
            var size = painter.Measure(shown.Items, look, OverlayWindow.ScaleOf(96, overlay, screens));
            places.Add(Snaps.Locate(overlay, screens, size.Width, size.Height) is { } corner ? new Rect(corner.X, corner.Y, size.Width, size.Height) : null);
        }

        return places;
    }
}
