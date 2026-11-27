using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Overlay;

/// <summary>
/// While an overlay is dragged, the spots it can snap to, as Windows marks them: each its own outline where it would go,
/// the one it would drop onto filled. The extension shows each as an overlay of its own, past thermi's, that takes no click;
/// the dragged overlay rises above them as it moves.
/// </summary>
internal sealed class ShellMarks
{
    /// <summary>The first id of a mark's, past every overlay's.</summary>
    public const uint FirstId = ThermiConfig.MaxOverlays;

    private const double Border = 2;
    private const double Fill = 40 / 255.0;
    private const double LitFill = 120 / 255.0;
    private const double Dark = 160 / 255.0;

    private readonly OverlayParts _parts;
    private readonly ShellScreens _screens;
    private readonly (int Width, int Height) _size;
    private readonly IReadOnlyList<(SnapConfig Snap, Position At)> _marks;
    private SnapConfig? _lit;
    private bool _shown;

    public ShellMarks(OverlayParts parts, ShellScreens screens, Orientation orientation, (int Width, int Height) size)
    {
        (_parts, _screens, _size) = (parts, screens, size);
        _marks = Snaps.Marks(screens.Screens, orientation, size.Width, size.Height);
    }

    /// <summary>Shows the marks, the one <paramref name="snap"/> names filled, or none; a mark is sent again only when it changed.</summary>
    public void Light(SnapConfig? snap)
    {
        if (_shown && snap == _lit)
        {
            return;
        }

        for (var at = 0; at < _marks.Count; at++)
        {
            var (each, place) = _marks[at];
            if (!_shown || each == snap || each == _lit)
            {
                Show(FirstId + (uint)at, place, lit: each == snap);
            }
        }

        (_lit, _shown) = (snap, true);
    }

    public void Hide()
    {
        for (var at = 0; at < _marks.Count; at++)
        {
            _ = _parts.Call(_parts.Link.Hide(FirstId + (uint)at));
        }
    }

    // White inside a dark line, so a mark shows on any wallpaper and bar; painted in thermi's pixels.
    private void Show(uint id, Position place, bool lit)
    {
        using var picture = new Picture(_size.Width, _size.Height);
        var scale = _screens.Scale;
        using (var cr = new Cairo.Context(picture.Surface))
        {
            var line = Border * scale;
            var box = new PillBox(line, line, _size.Width - (2 * line), _size.Height - (2 * line));
            PainterShapes.Rounded(cr, box, Math.Min(box.Width, box.Height) / 2);
            cr.SetSourceRgba(1, 1, 1, lit ? LitFill : Fill);
            cr.FillPreserve();
            cr.SetSourceRgba(0, 0, 0, Dark);
            cr.LineWidth = line * 2;
            cr.StrokePreserve();
            cr.SetSourceRgba(1, 1, 1, 1);
            cr.LineWidth = line;
            cr.Stroke();
        }

        var (x, y) = _screens.Stage(place);
        using var pixels = picture.Bytes();
        _ = _parts.Call(_parts.Link.Show(id, new ShellPicture(pixels, picture.Width, picture.Height, picture.Stride, scale, []), x, y));
    }
}
