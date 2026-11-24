using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>How much an overlay that fades near the pointer shows, so what lies under it shows while it still takes clicks.</summary>
public static class PointerFade
{
    /// <summary>Pixels at 100 % from the overlay within which it starts to fade.</summary>
    public const double Reach = 96;

    /// <summary>The opacity with the pointer on the overlay.</summary>
    public const double Least = 0.25;

    /// <summary>
    /// The opacity of an overlay shown at <paramref name="at"/>, with the pointer at <paramref name="cursor"/>: whole beyond
    /// <see cref="Reach"/> times <paramref name="scale"/>, <see cref="Least"/> on it, and in between as the pointer nears.
    /// Whole while it does not fade, while the window is open, while it is <paramref name="dragged"/>, and under
    /// click-through, whose clicks reach what lies under it anyway.
    /// </summary>
    public static double Opacity(OverlayConfig overlay, PixelRect at, Position cursor, double scale, bool windowOpen, bool dragged)
    {
        if (!overlay.Fade || overlay.ClickThrough || windowOpen || dragged)
        {
            return 1;
        }

        // The rectangle's right and bottom edges lie just outside it.
        double x = Math.Max(Math.Max(at.Left - cursor.X, cursor.X - (at.Right - 1)), 0);
        double y = Math.Max(Math.Max(at.Top - cursor.Y, cursor.Y - (at.Bottom - 1)), 0);
        var reach = Reach * scale;
        var distance = Math.Sqrt((x * x) + (y * y));
        return distance >= reach ? 1 : Least + ((1 - Least) * distance / reach);
    }
}
