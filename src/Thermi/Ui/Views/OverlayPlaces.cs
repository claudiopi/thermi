using System.Drawing;
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
        var drawn = Enumerable.Range(0, config.Overlays.Count).Select(at =>
        {
            var overlay = config.Overlays[at];
            var shown = ThermiMark.Overlay(config, at, input, windowOpenedOnce: true);
            return (shown.Items, Look: OverlayLook.Of(overlay.Style, input.LightTaskbar), Scale: OverlayWindow.ScaleOf(96, overlay, screens));
        }).ToList();
        var sizes = drawn.Select(each => (PixelRect?)Box(painter.Measure(each.Items, each.Look, each.Scale))).ToList();

        // Each in its turn, one grown past the room the others leave on its taskbar shows smaller, as the overlays draw it.
        for (var at = 0; at < sizes.Count; at++)
        {
            var (index, (items, look, scale)) = (at, drawn[at]);
            var fitted = painter.Within(items, look, scale, size => TaskbarRow.Room(config.Overlays, index, screens, size, sizes));
            sizes[at] = fitted < scale ? Box(painter.Measure(items, look, fitted)) : sizes[at];
        }

        // Overlays sharing a taskbar are laid out together, as the overlays place themselves.
        return [.. sizes.Select((size, at) => TaskbarRow.Locate(config.Overlays, at, screens, (size!.Value.Width, size.Value.Height), sizes) is { } corner
            ? new Rect(corner.X, corner.Y, size.Value.Width, size.Value.Height)
            : null)];
    }

    private static PixelRect Box(Size size) => new(0, 0, size.Width, size.Height);
}
