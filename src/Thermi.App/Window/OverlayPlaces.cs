using Thermi.App.Painting;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.App.Window;

/// <summary>
/// Where each overlay shows, worked out as its own window places it: the items it shows, measured as its painter keeps
/// their room at its monitor's scale, at the place its snap or position gives that size. Worked out from the settings
/// the window holds, it never waits for the overlay to move. Each OS measures with its own painter, made by
/// <c>painter</c>.
/// </summary>
internal static class OverlayPlaces
{
    /// <summary>
    /// Where each overlay shows while the window is open, which <paramref name="input"/> says, kept to its room on a taskbar
    /// with parts by its size with the window closed, as <see cref="TaskbarRow.RoomOf"/> keeps it.
    /// </summary>
    public static IReadOnlyList<OverlayPlace?> Of(ThermiConfig config, TrayInput input, IReadOnlyList<Screen> screens, Func<IOverlayPainter> painter)
    {
        using var measure = painter();
        var closed = input.WindowOpen ? Fit(measure, config, input with { WindowOpen = false }, screens) : null;
        var shown = TaskbarRow.Fitted(config.Overlays, screens, [], Fit(measure, config, input, screens), closed).Fitted;
        return [.. shown.Select((size, at) => size is { } box && TaskbarRow.Locate(config.Overlays, at, screens, (box.Width, box.Height), shown) is { } corner
            ? new OverlayPlace(corner.X, corner.Y, box.Width, box.Height, TaskbarRow.Beside(
                config.Overlays[at], screens, new PixelRect(corner.X, corner.Y, corner.X + box.Width, corner.Y + box.Height)))
            : null)];
    }

    /// <summary>
    /// The settings after an edit or a gesture, as <see cref="TaskbarRow.Settled"/> gives them, the others measured as they
    /// showed <paramref name="before"/> it, as each overlay fits itself beside the others' windows.
    /// </summary>
    public static EditResult Settled(ThermiConfig before, ThermiConfig after, TrayInput input, IReadOnlyList<Screen> screens, Func<IOverlayPainter> painter)
    {
        using var measure = painter();
        var shown = TaskbarRow.Fitted(before.Overlays, screens, [], Fit(measure, before, input, screens)).Fitted;
        return TaskbarRow.Settled(before, after, screens, shown, Fit(measure, after, input, screens));
    }

    /// <summary>
    /// As <see cref="Settled(ThermiConfig, ThermiConfig, TrayInput, IReadOnlyList{Screen}, Func{IOverlayPainter})"/>, from
    /// the overlays' own process: the others as their windows, <paramref name="shown"/>, box them now.
    /// </summary>
    public static EditResult Settled(
        ThermiConfig before, ThermiConfig after, TrayInput input, IReadOnlyList<Screen> screens, IReadOnlyList<PixelRect?> shown, IOverlayPainter painter) =>
        TaskbarRow.Settled(before, after, screens, shown, Fit(painter, after, input, screens));

    /// <summary>
    /// Whether each overlay off a taskbar shows smaller than its size so as to fit its monitor's work area, as its window
    /// fits it at any size, measured by <paramref name="painterOf"/> its painter, which keeps the answer until its items
    /// change.
    /// </summary>
    public static IReadOnlyList<bool> Fitted(ThermiConfig config, TrayInput input, Func<IReadOnlyList<Screen>> screens, Func<int, IOverlayPainter> painterOf)
    {
        var on = screens();
        bool Shrinks(OverlayConfig overlay, int at)
        {
            var items = ThermiMark.Overlay(config, at, input, windowOpenedOnce: true).Items;
            var scale = ScaleOf(Dpi, overlay, on);
            return painterOf(at).Fitting(items, OverlayLook.Of(overlay.Style, input.LightTaskbar), scale, Snaps.Most(overlay, on)) < scale;
        }

        return [.. config.Overlays.Select(Shrinks)];
    }

    /// <summary>A monitor's pixels at 100 %, the scale of one that says none.</summary>
    public const uint Dpi = 96;

    /// <summary>
    /// The scale the overlay is drawn at, times the size it shows at: the monitor's it is about to show on among
    /// <paramref name="screens"/>, so a snap to a monitor of another scale paints right at once, or its window's,
    /// <paramref name="dpi"/>, while it knows none.
    /// </summary>
    public static float ScaleOf(uint dpi, OverlayConfig overlay, IReadOnlyList<Screen> screens) =>
        (float)((Snaps.ScreenOf(overlay, screens) is { } screen ? screen.Scale : dpi / (double)Dpi) * Snaps.SizeShown(overlay, screens));

    // Each overlay's items measured as its painter keeps their room at its monitor's scale: at its own size, or within a room.
    private static OverlayFit Fit(IOverlayPainter painter, ThermiConfig config, TrayInput input, IReadOnlyList<Screen> screens) => (at, room) =>
    {
        var overlay = config.Overlays[at];
        var items = ThermiMark.Overlay(config, at, input, windowOpenedOnce: true).Items;
        var look = OverlayLook.Of(overlay.Style, input.LightTaskbar);
        var scale = painter.Fitting(items, look, ScaleOf(Dpi, overlay, screens), Snaps.Most(overlay, screens));
        var size = painter.Measure(items, look, room is null ? scale : painter.Within(items, look, scale, room));
        return new PixelRect(0, 0, size.Width, size.Height);
    };
}
