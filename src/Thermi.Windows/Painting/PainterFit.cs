using System.Drawing;
using Thermi.Core.Look;

namespace Thermi.Windows.Painting;

/// <summary>
/// The scale an overlay shows at where it would not fit at its own, beside others on a taskbar or on its monitor, as its
/// painter measures it with GDI+.
/// </summary>
internal static class PainterFit
{
    /// <summary>
    /// <paramref name="scale"/>, or the smaller one at which the items show as wide as <paramref name="room"/> allows for
    /// their size at it, as an overlay grown past the room the others beside it on its taskbar leave; without a room, as
    /// for one alone, <paramref name="scale"/> unmeasured.
    /// </summary>
    public static float Within(this Painter painter, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, Func<(int Width, int Height), int?>? room)
    {
        if (room is null)
        {
            return scale;
        }

        Size size = painter.Measure(items, look, scale);
        return room((size.Width, size.Height)) is { } most && size.Width > most ? scale * Math.Max(1, most) / size.Width : scale;
    }

    /// <summary>
    /// <paramref name="scale"/>, or the smaller one at which the items fit within <paramref name="most"/>, as an overlay
    /// that would show larger than its monitor's work area; without one, <paramref name="scale"/> unmeasured. Text does
    /// not shrink quite in step with the scale, so it measures again until they fit.
    /// </summary>
    public static float Fitting(this Painter painter, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, (int Width, int Height)? most)
    {
        if (most is not var (width, height))
        {
            return scale;
        }

        for (var round = 0; round < 4; round++)
        {
            Size size = painter.Measure(items, look, scale);
            if (size.Width <= width && size.Height <= height)
            {
                break;
            }

            scale *= Math.Min((float)width / size.Width, (float)height / size.Height);
        }

        return scale;
    }
}
