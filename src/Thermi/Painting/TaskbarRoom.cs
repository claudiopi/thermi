using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows;

namespace Thermi.Painting;

/// <summary>
/// Whether an overlay on a taskbar spot fits the room beside the app buttons, which UI Automation reads, its labels and
/// each value's widest form measured as the painter draws them. One that does not sits just off the taskbar, and the
/// window says so.
/// </summary>
internal static class TaskbarRoom
{
    private static readonly IReadOnlyDictionary<string, Reading> NoReadings = new Dictionary<string, Reading>(StringComparer.Ordinal);

    /// <summary>The window's question: true when the overlay, as it shows now, does not fit beside the app buttons.</summary>
    public static bool Crowded(Painter painter, TaskbarApps apps, ThermiConfig config, int index, Snapshot? snapshot, IReadOnlyList<Screen> screens)
    {
        if (index < 0 || index >= config.Overlays.Count || config.Overlays[index] is not { Snapped: { } snap } overlay || Snaps.ScreenOf(snap, screens) is not { Taskbar: { } bar } screen)
        {
            return false;
        }

        var texts = Texts.For(config.Language, CultureInfo.CurrentUICulture);
        var items = OverlayLayout.Build(overlay, snapshot is null ? NoReadings : SensorMap.Read(snapshot, overlay), CultureInfo.CurrentCulture, texts, config.Temperature);
        var look = OverlayLook.Of(overlay.Style, Desktop.LightTaskbar());
        var read = screen with { Buttons = apps.LeftButtons(bar), Apps = apps.Extent(bar) };
        return Crowded(painter, items, look, (float)(screen.Scale * overlay.Size), overlay, [read]);
    }

    /// <summary>
    /// True when <paramref name="items"/>, drawn in <paramref name="look"/> at <paramref name="scale"/>, do not fit beside
    /// the app buttons of the screens' taskbar the overlay sits on; false off the taskbar, on its center, or where the
    /// buttons were not read.
    /// </summary>
    public static bool Crowded(Painter painter, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, OverlayConfig overlay, IReadOnlyList<Screen> screens)
    {
        if (overlay.Snapped is not { } snap || Snaps.ScreenOf(snap, screens) is not { Apps: { } extent } screen || items.Count == 0)
        {
            return false;
        }

        if (Snaps.Room(screen, Snaps.Shown(snap.Spot, screen, overlay.Style.Orientation), extent) is not { } room)
        {
            return false;
        }

        // Each pill's own padding is room the items cannot take.
        var (padding, gap) = Painter.Spacing(scale);
        var pills = OverlayLayout.Groups(items, look.Layout).Count;
        return !TaskbarFit.Fits(painter.Measure(items, look, scale), gap, room - (pills * padding));
    }
}
