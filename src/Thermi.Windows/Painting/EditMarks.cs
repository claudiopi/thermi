using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Settings;

namespace Thermi.Windows.Painting;

/// <summary>
/// What the open window marks on an overlay: its fade, and on the overlay being edited the bar beside the value selected
/// and, fainter, the one pointed at, in the accent color.
/// </summary>
internal sealed record EditMarks(EditFade Fade, string? Selected, string? Pointed, Rgb Accent)
{
    public static EditMarks None { get; } = new(EditFade.None, null, null, default);

    // A short bar lying under the value, as a tab's mark, so it never reads as a text cursor beside the digits.
    private const float BarWidth = 16;
    private const float BarHeight = 3;

    // From the pill's lower edge to the bar's foot, inside its outer padding.
    private const float BarRise = 6;

    // A value's level mark, as a warning's underline, runs where the bar would: the bar steps down from it.
    private const float LevelRoom = 1.5f;

    private const int PointedAlpha = 128;

    /// <summary>The look with its fade, or, <paramref name="held"/> in a drag, whole, as Core's <see cref="EditLooks.Applied"/> says.</summary>
    public OverlayLook Applied(OverlayLook look, bool held = false) => EditLooks.Applied(look, Fade, held);

    /// <summary>Draws the bars beside the values drawn at <paramref name="places"/>, opaque over the faded overlay.</summary>
    public void Draw(Bitmap bitmap, IReadOnlyList<PaintedItem> places, IReadOnlyList<OverlayItem> items, float scale)
    {
        if (Selected is null && Pointed is null)
        {
            return;
        }

        using var g = Graphics.FromImage(bitmap);
        Bar(g, places, items, Pointed, PointedAlpha, scale);
        Bar(g, places, items, Selected, 255, scale);
    }

    private void Bar(Graphics g, IReadOnlyList<PaintedItem> places, IReadOnlyList<OverlayItem> items, string? id, int alpha, float scale)
    {
        if (id is null || places.FirstOrDefault(place => place.Id == id) is not { } place)
        {
            return;
        }

        var leveled = items.FirstOrDefault(item => item.Id == id)?.Level is { } level && level != Level.Normal;
        var (width, height) = (BarWidth * scale, BarHeight * scale);
        var (pill, value) = (place.Pill.Rect(), place.Value.Rect());
        var bottom = pill.Bottom - ((BarRise - (leveled ? LevelRoom : 0)) * scale);
        using var brush = new SolidBrush(Color.FromArgb(alpha, PainterShapes.Drawn(Accent)));
        using var bar = PainterShapes.Rounded(new RectangleF(value.Left + ((value.Width - width) / 2), bottom - height, width, height), height / 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, bar);
    }
}
