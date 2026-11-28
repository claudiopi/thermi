using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Linux.Painting;

/// <summary>
/// What the open settings window marks on the overlay it edits, as Windows marks it: a bar under the value selected and,
/// fainter, under the one pointed at, in the accent color, opaque over a faded overlay.
/// </summary>
internal static class EditBars
{
    // A short bar lying under the value, as a tab's mark, so it never reads as a text cursor beside the digits.
    private const double BarWidth = 16;
    private const double BarHeight = 3;

    // From the pill's lower edge to the bar's foot, inside its outer padding.
    private const double BarRise = 6;

    // A value's level mark, as a warning's underline, runs where the bar would: the bar steps down from it.
    private const double LevelRoom = 1.5;

    private const double PointedAlpha = 128 / 255.0;

    public static void Draw(
        Picture picture, IReadOnlyList<PaintedItem> places, IReadOnlyList<OverlayItem> items, (string? Selected, string? Pointed) marked, Rgb accent,
        double scale)
    {
        if (marked is (null, null))
        {
            return;
        }

        using var cr = new Cairo.Context(picture.Surface);
        Bar(cr, places, items, marked.Pointed, PointedAlpha, accent, scale);
        Bar(cr, places, items, marked.Selected, 1, accent, scale);
    }

    private static void Bar(
        Cairo.Context cr, IReadOnlyList<PaintedItem> places, IReadOnlyList<OverlayItem> items, string? id, double alpha, Rgb accent, double scale)
    {
        if (id is null || places.FirstOrDefault(place => place.Id == id) is not { } place)
        {
            return;
        }

        var leveled = items.FirstOrDefault(item => item.Id == id)?.Level is { } level && level != Level.Normal;
        var (width, height) = (BarWidth * scale, BarHeight * scale);
        var bottom = place.Pill.Bottom - ((BarRise - (leveled ? LevelRoom : 0)) * scale);
        var bar = new PillBox(place.Value.Left + ((place.Value.Width - width) / 2), bottom - height, width, height);
        PainterShapes.Rounded(cr, bar, height / 2);
        PainterShapes.Source(cr, accent, alpha);
        cr.Fill();
    }
}
