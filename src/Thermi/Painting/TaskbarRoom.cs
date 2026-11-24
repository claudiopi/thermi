using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows;

namespace Thermi.Painting;

/// <summary>
/// How much of a taskbar's row an overlay keeps, its labels and each value's widest form measured as the painter draws
/// them, and which overlays the rows beside the app buttons, which UI Automation reads, have no room for. One without
/// room sits just off the taskbar, and the window says so.
/// </summary>
internal static class TaskbarRoom
{
    private static readonly IReadOnlyDictionary<string, Reading> NoReadings = new Dictionary<string, Reading>(StringComparer.Ordinal);

    /// <summary>
    /// The width <paramref name="items"/>, drawn in <paramref name="look"/> at <paramref name="scale"/>, keep on a taskbar,
    /// never less than the <paramref name="shown"/> width they are drawn at now.
    /// </summary>
    public static int Width(Painter painter, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, int shown)
    {
        // Each pill's own padding is room the items cannot take.
        var (padding, gap) = Painter.Spacing(scale);
        var pills = OverlayLayout.Groups(items, look.Layout).Count;
        return Math.Max(shown, (int)Math.Ceiling(TaskbarFit.Width(painter.Measure(items, look, scale), gap) + (pills * padding)));
    }

    /// <summary>The window's question: the overlays that, as they show now, have no room on their taskbar.</summary>
    public static IReadOnlyList<int> Crowded(Painter painter, TaskbarApps apps, ThermiConfig config, Snapshot? snapshot, IReadOnlyList<Screen> screens)
    {
        IReadOnlyList<Screen> read = [.. screens.Select(screen => screen.Taskbar is { } bar ? screen with { Apps = apps.Extent(bar) } : screen)];
        var texts = Texts.For(config.Language, CultureInfo.CurrentUICulture);
        int?[] widths = [.. config.Overlays.Select(overlay => WidthOf(painter, config, overlay, texts, snapshot, read))];
        return [.. Enumerable.Range(0, widths.Length).Where(overlay => TaskbarRow.Place(config.Overlays, overlay, read, widths) is { Left: null })];
    }

    // A hidden overlay, or one off the taskbars, keeps no room there.
    private static int? WidthOf(Painter painter, ThermiConfig config, OverlayConfig overlay, Texts texts, Snapshot? snapshot, IReadOnlyList<Screen> screens)
    {
        if (overlay.Hidden || overlay.Snapped is not { } snap || Snaps.ScreenOf(snap, screens) is not { Taskbar: not null } screen)
        {
            return null;
        }

        var readings = snapshot is null ? NoReadings : SensorMap.Read(snapshot, overlay);
        var items = OverlayLayout.Build(overlay, readings, CultureInfo.CurrentCulture, texts, config.Temperature);
        var look = OverlayLook.Of(overlay.Style, Desktop.LightTaskbar());
        return items.Count == 0 ? null : Width(painter, items, look, (float)(screen.Scale * overlay.Size), 0);
    }
}
