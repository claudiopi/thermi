using System.Drawing;
using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
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
        var shown = Sizes(config, input, screens, []);
        return [.. shown.Select((size, at) => size is { } box && TaskbarRow.Locate(config.Overlays, at, screens, (box.Width, box.Height), shown) is { } corner
            ? new Rect(corner.X, corner.Y, box.Width, box.Height)
            : null)];
    }

    /// <summary>
    /// The settings after an edit or a gesture, with each overlay it pushed along a taskbar given the place it shows at as its
    /// own (<see cref="TaskbarRow.Settle"/>), the others measured as they showed <paramref name="before"/> it, as each overlay
    /// fits itself beside the others' windows.
    /// </summary>
    public static ThermiConfig Settled(ThermiConfig before, ThermiConfig after, TrayInput input, IReadOnlyList<Screen> screens) =>
        Settled(after, input, screens, Sizes(before, input, screens, []));

    /// <summary>
    /// As <see cref="Settled(ThermiConfig, ThermiConfig, TrayInput, IReadOnlyList{Screen})"/>, from the overlays' own
    /// process: the others as their windows, <paramref name="shown"/>, box them now.
    /// </summary>
    public static ThermiConfig Settled(ThermiConfig after, Overlay.OverlayScene scene, CultureInfo culture, IReadOnlyList<PixelRect?> shown) =>
        Settled(
            after,
            new TrayInput(scene.Snapshot, scene.Service, culture, Texts.For(after.Language, CultureInfo.CurrentUICulture), scene.LightTaskbar, ""),
            scene.Screens,
            shown);

    private static ThermiConfig Settled(ThermiConfig after, TrayInput input, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown)
    {
        var overlays = TaskbarRow.Settle(after.Overlays, screens, Sizes(after, input, screens, shown));
        return overlays.SequenceEqual(after.Overlays) ? after : after with { Overlays = overlays };
    }

    // Each in its turn, one grown past the room the others leave on its taskbar shows smaller, as the overlays draw it; the
    // others not yet fitted count as they showed, or at their own size.
    private static List<PixelRect?> Sizes(ThermiConfig config, TrayInput input, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown)
    {
        using var painter = new Painter();
        var drawn = Enumerable.Range(0, config.Overlays.Count).Select(at =>
        {
            var overlay = config.Overlays[at];
            var items = ThermiMark.Overlay(config, at, input, windowOpenedOnce: true).Items;
            return (Items: items, Look: OverlayLook.Of(overlay.Style, input.LightTaskbar), Scale: OverlayWindow.ScaleOf(96, overlay, screens));
        }).ToList();
        var natural = drawn.Select(each => Box(painter.Measure(each.Items, each.Look, each.Scale))).ToList();

        // A hidden overlay's window shows nowhere, so it takes no room from the others on its taskbar.
        var sizes = natural.Select((size, at) => config.Overlays[at].Hidden ? null : shown.ElementAtOrDefault(at) ?? size).ToList();
        for (var at = 0; at < sizes.Count; at++)
        {
            if (sizes[at] is null)
            {
                continue;
            }

            var (items, look, scale) = drawn[at];
            var fitted = painter.Within(items, look, scale, TaskbarRow.RoomOf(config.Overlays, at, screens, sizes));
            sizes[at] = fitted < scale ? Box(painter.Measure(items, look, fitted)) : natural[at];
        }

        return sizes;
    }

    private static PixelRect? Box(Size size) => new PixelRect(0, 0, size.Width, size.Height);
}
