using System.Drawing;
using Thermi.Core.Look;

namespace Thermi.App.Painting;

/// <summary>
/// The scale an overlay shows at where it would not fit at its own, beside others on a taskbar or on its monitor, as its
/// painter measures it.
/// </summary>
internal static class PainterFit
{
    /// <summary>
    /// <paramref name="scale"/>, or the smaller one at which the items show as wide as <paramref name="room"/> allows for
    /// their size at it, as an overlay grown past the room the others beside it on its taskbar leave; without a room, as
    /// for one alone, <paramref name="scale"/> unmeasured.
    /// </summary>
    public static float Within(
        this IOverlayPainter painter, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, Func<(int Width, int Height), int?>? room)
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
    /// not shrink quite in step with the scale, so it measures again until they fit. The painter keeps the last answer for
    /// what it measured, so an overlay that paints the same items each second measures them once.
    /// </summary>
    public static float Fitting(this IOverlayPainter painter, IReadOnlyList<OverlayItem> items, OverlayLook look, float scale, (int Width, int Height)? most)
    {
        if (most is not var (width, height))
        {
            return scale;
        }

        // What its size reads: each value keeps the room of its widest form, so the values themselves need not match.
        var measured = (Scale: scale, Most: most, Look: look, Items: string.Join(';', items.Select(
            item => (item.Label, item.Room, item.Tag, item.Graph is null, item.Lit is null))));
        if (painter.Fitted is { } fitted && fitted.Measured == measured)
        {
            return fitted.Scale;
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

        painter.Fitted = (measured, scale);
        return scale;
    }
}
