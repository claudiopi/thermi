using System.Globalization;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows;

namespace Thermi.Painting;

/// <summary>
/// Which overlays the rows beside the app buttons, which UI Automation reads, have no room for, each keeping the width the
/// painter draws it at, its values at their widest forms. One without room sits just off the taskbar, and the window
/// says so.
/// </summary>
internal static class TaskbarRoom
{
    private static readonly IReadOnlyDictionary<string, Reading> NoReadings = new Dictionary<string, Reading>(StringComparer.Ordinal);

    /// <summary>The window's question: the overlays that, as they show now, have no room on their taskbar.</summary>
    public static IReadOnlyList<int> Crowded(Painter painter, TaskbarApps apps, ThermiConfig config, Snapshot? snapshot, IReadOnlyList<Screen> screens)
    {
        IReadOnlyList<Screen> read = [.. screens.Select(screen => screen.Taskbar is { } bar ? screen with { Apps = apps.Extent(bar) } : screen)];
        var texts = Texts.For(config.Language, CultureInfo.CurrentUICulture);
        int?[] widths = [.. config.Overlays.Select(overlay => WidthOf(painter, config, overlay, texts, snapshot, read))];
        return [.. Enumerable.Range(0, widths.Length).Where(overlay => TaskbarRow.Place(config.Overlays, overlay, read, widths) is { Left: null })];
    }

    /// <summary>
    /// The map's question: the taskbar spots, on every display with a taskbar, where <paramref name="overlay"/> would have
    /// no room as it shows now, beside the others where they are. The main display's are named without it, as the file does.
    /// </summary>
    public static IReadOnlyList<SnapConfig> Roomless(
        Painter painter,
        TaskbarApps apps,
        ThermiConfig config,
        int overlay,
        Snapshot? snapshot,
        IReadOnlyList<Screen> screens)
    {
        if (overlay < 0 || overlay >= config.Overlays.Count)
        {
            return [];
        }

        var spots = screens.Where(screen => screen.Taskbar is not null)
            .SelectMany(screen => new[] { SnapSpot.TaskbarLeft, SnapSpot.TaskbarCenter, SnapSpot.TaskbarRight }
                .Select(spot => new SnapConfig(spot, screen.Primary ? null : screen.Id)));
        return [.. spots.Where(snap =>
        {
            var there = config.Overlays[overlay] with { Snap = snap, Position = null, Hidden = false };
            var moved = config with { Overlays = [.. config.Overlays.Select((one, at) => at == overlay ? there : one)] };
            return Crowded(painter, apps, moved, snapshot, screens).Contains(overlay);
        })];
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
        return items.Count == 0 ? null : painter.Width(items, look, (float)(screen.Scale * overlay.Size));
    }
}
