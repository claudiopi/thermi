using System.Drawing;
using Thermi.Core.Look;
using Thermi.Core.Settings;

namespace Thermi.Painting;

/// <summary>How an overlay's frame or whole fades while the window edits another one.</summary>
internal enum Fade
{
    None,

    /// <summary>Only the pills fade, so the values and their levels read as before.</summary>
    Frame,

    /// <summary>The whole overlay fades, as one whose pills are the opposite of the taskbar's would stand out otherwise.</summary>
    Whole,
}

/// <summary>
/// What the open window marks on an overlay: its fade, and on the overlay being edited the bar beside the value selected
/// and, fainter, the one pointed at, in the accent color.
/// </summary>
internal sealed record EditMarks(Fade Fade, string? Selected, string? Pointed, Rgb Accent)
{
    public static EditMarks None { get; } = new(Fade.None, null, null, default);

    private const double FrameFade = 0.3;
    private const double WholeFade = 0.5;
    private const float BarWidth = 3;
    private const float BarHeight = 16;
    private const float BarGap = 3;

    // Room a value's level mark takes around it, which the bar steps aside from.
    private const float LevelRoom = 4;

    private const int PointedAlpha = 128;

    /// <summary>
    /// The look with its fade, or, <paramref name="held"/> in a drag over other overlays and the window's fades, with a
    /// background of its own whatever the look's opacity.
    /// </summary>
    public OverlayLook Applied(OverlayLook look, bool held = false) => held ? look with { Background = 1, Opacity = 1 } : Fade switch
    {
        Fade.Frame => look with { Background = look.Background * FrameFade },
        Fade.Whole => look with { Opacity = look.Opacity * WholeFade },
        Fade.None => look,
        _ => look,
    };

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
        var right = place.Value.Left - ((BarGap + (leveled ? LevelRoom : 0)) * scale);
        var height = BarHeight * scale;
        using var brush = new SolidBrush(Color.FromArgb(alpha, PainterShapes.Drawn(Accent)));
        g.FillRectangle(brush, right - (BarWidth * scale), place.Value.Top + ((place.Value.Height - height) / 2), BarWidth * scale, height);
    }
}
