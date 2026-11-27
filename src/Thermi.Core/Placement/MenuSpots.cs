using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>The side of an overlay its menu opens on.</summary>
public enum MenuSide
{
    Below,
    Above,
}

/// <summary>
/// Where an overlay's menu opens: against <see cref="Anchor"/>, the overlay's height at the pointer's x, on the side
/// <see cref="Side"/> says, as a desktop that points a menu at a box takes it.
/// </summary>
public readonly record struct MenuSpot(PixelRect Anchor, MenuSide Side)
{
    /// <summary>The menu's corner on the overlay's edge, for a desktop that opens a menu at a point: its top-left below, its bottom-left above.</summary>
    public Position At => new(Anchor.Left, Side == MenuSide.Below ? Anchor.Bottom : Anchor.Top);
}

/// <summary>Where an overlay's menu opens on every OS: beside the overlay, so it covers none of its pills.</summary>
public static class MenuSpots
{
    /// <summary>
    /// The menu of an overlay shown at <paramref name="overlay"/>, opened with the pointer at <paramref name="cursor"/> on
    /// the monitor <paramref name="screen"/>: at the pointer's x, below the overlay where a menu <paramref name="height"/>
    /// pixels tall fits, else above where it fits, else on the side with more room. A desktop that knows no menu's height
    /// before it shows passes 0, and moves the menu to the other side itself where it does not fit.
    /// </summary>
    public static MenuSpot Beside(PixelRect overlay, Position cursor, PixelRect screen, int height = 0)
    {
        var x = Math.Clamp(cursor.X, overlay.Left, Math.Max(overlay.Left, overlay.Right - 1));
        var (below, above) = (screen.Bottom - overlay.Bottom, overlay.Top - screen.Top);
        var side = below >= height || (above < height && below >= above) ? MenuSide.Below : MenuSide.Above;
        return new MenuSpot(new PixelRect(x, overlay.Top, x + 1, overlay.Bottom), side);
    }
}
