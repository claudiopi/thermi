using System.Globalization;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Overlay;
using Thermi.Windows.Painting;

namespace Thermi.Windows.Ui.Views;

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
        var shown = TaskbarRow.Fitted(config.Overlays, screens, [], Fit(painter, config, input, screens)).Fitted;
        return [.. shown.Select((size, at) => size is { } box && TaskbarRow.Locate(config.Overlays, at, screens, (box.Width, box.Height), shown) is { } corner
            ? new Rect(corner.X, corner.Y, box.Width, box.Height)
            : null)];
    }

    /// <summary>
    /// The settings after an edit or a gesture, as <see cref="TaskbarRow.Settled"/> gives them, the others measured as they
    /// showed <paramref name="before"/> it, as each overlay fits itself beside the others' windows.
    /// </summary>
    public static EditResult Settled(ThermiConfig before, ThermiConfig after, TrayInput input, IReadOnlyList<Screen> screens)
    {
        using var painter = new Painter();
        var shown = TaskbarRow.Fitted(before.Overlays, screens, [], Fit(painter, before, input, screens)).Fitted;
        return TaskbarRow.Settled(before, after, screens, shown, Fit(painter, after, input, screens));
    }

    /// <summary>
    /// As <see cref="Settled(ThermiConfig, ThermiConfig, TrayInput, IReadOnlyList{Screen})"/>, from the overlays' own
    /// process: the others as their windows, <paramref name="shown"/>, box them now.
    /// </summary>
    public static EditResult Settled(
        ThermiConfig before, ThermiConfig after, Overlay.OverlayScene scene, CultureInfo culture, IReadOnlyList<PixelRect?> shown)
    {
        var input = new TrayInput(scene.Snapshot, scene.Service, culture, Texts.For(after.Language, CultureInfo.CurrentUICulture), scene.LightTaskbar, "");
        using var painter = new Painter();
        return TaskbarRow.Settled(before, after, scene.Screens, shown, Fit(painter, after, input, scene.Screens));
    }

    // Each overlay's items measured as its painter keeps their room at its monitor's scale: at its own size, or within a room.
    private static OverlayFit Fit(Painter painter, ThermiConfig config, TrayInput input, IReadOnlyList<Screen> screens) => (at, room) =>
    {
        var overlay = config.Overlays[at];
        var items = ThermiMark.Overlay(config, at, input, windowOpenedOnce: true).Items;
        var look = OverlayLook.Of(overlay.Style, input.LightTaskbar);
        var scale = OverlayWindow.ScaleOf(96, overlay, screens);
        var size = painter.Measure(items, look, room is null ? scale : painter.Within(items, look, scale, room));
        return new PixelRect(0, 0, size.Width, size.Height);
    };
}
