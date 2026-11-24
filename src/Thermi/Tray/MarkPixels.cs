using System.Drawing;
using System.Drawing.Drawing2D;
using Thermi.Core.Look;
using Thermi.Painting;

namespace Thermi.Tray;

/// <summary>
/// thermi's mark placed by hand, pixel by pixel, at 16 and 24 px, where the vector would blur into a smudge: the body
/// row by row, the eyes as cells cut out of it, and the line below.
/// </summary>
internal sealed class MarkPixels
{
    private static readonly MarkPixels Small = new(
        16,
        [(4, 5, 10), (5, 3, 12), (6, 2, 13), (7, 2, 13), (8, 1, 14), (9, 1, 14), (10, 1, 14), (11, 1, 14)],
        (13, 3, 12),
        Eyes((7, 6), (11, 6), "##", "##"),
        Eyes((7, 6), (11, 6), "##", "##", "##"),
        Eyes((7, 7), (11, 7), "##"));

    private static readonly MarkPixels Large = new(
        24,
        [(7, 8, 15), (8, 6, 17), (9, 4, 19), (10, 3, 20), (11, 3, 20), (12, 2, 21), (13, 2, 21), (14, 2, 21), (15, 2, 21), (16, 2, 21), (17, 2, 21), (18, 2, 21)],
        (20, 4, 19),
        Eyes((10, 10), (16, 10), ".##.", "####", "####", ".##."),
        Eyes((10, 10), (16, 10), ".##.", "####", "####", "####", ".##."),
        Eyes((10, 12), (16, 12), "#..#", ".##."));

    private readonly int _size;
    private readonly (int Y, int From, int To)[] _body;
    private readonly (int Y, int From, int To) _line;
    private readonly Dictionary<MarkEyes, HashSet<Point>> _eyes;

    private MarkPixels(int size, (int Y, int From, int To)[] body, (int Y, int From, int To) line, HashSet<Point> open, HashSet<Point> wide, HashSet<Point> closed)
    {
        (_size, _body, _line) = (size, body, line);
        _eyes = new() { [MarkEyes.Open] = open, [MarkEyes.Wide] = wide, [MarkEyes.Closed] = closed };
    }

    /// <summary>
    /// The hand-placed mark for an icon of <paramref name="size"/>: the 16 px one up to 23 px and the 24 px one up to
    /// 31 px, centered, so 125 % and 175 % stay as sharp; null from 32 px, where the vector is sharp enough.
    /// </summary>
    public static MarkPixels? For(int size) => size switch
    {
        < 24 => Small,
        < 32 => Large,
        _ => null,
    };

    /// <summary>Paints the mark at <paramref name="box"/>'s top left corner, centered in its width.</summary>
    public void Draw(Graphics g, MarkLook look, Rgb ink, RectangleF box)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var offset = new Point((int)box.Left + (((int)box.Width - _size) / 2), (int)box.Top + (((int)box.Height - _size) / 2));
        using var inked = new SolidBrush(PainterShapes.Drawn(ink));
        var eyes = _eyes[look.Eyes];
        foreach (var (y, from, to) in _body)
        {
            for (var x = from; x <= to; x++)
            {
                var eye = eyes.Contains(new Point(x, y));
                if (look.Stops is null && eye)
                {
                    continue;
                }

                using var brush = look.Stops is null ? null : new SolidBrush(PainterShapes.Drawn(eye ? MarkLook.EyeInk : look.At((x + 0.5 - 1) / (_size - 1))));
                g.FillRectangle(brush ?? inked, offset.X + x, offset.Y + y, 1, 1);
            }
        }

        g.FillRectangle(inked, offset.X + _line.From, offset.Y + _line.Y, _line.To - _line.From + 1, 1);
        g.Restore(state);
    }

    // Each eye's cells, the same pattern placed at each eye's top left corner.
    private static HashSet<Point> Eyes((int X, int Y) left, (int X, int Y) right, params string[] pattern) =>
    [
        .. new[] { left, right }.SelectMany(corner => pattern.SelectMany((row, dy) => row.Select((cell, dx) => (cell, dx)).Where(each => each.cell == '#').Select(each => new Point(corner.X + each.dx, corner.Y + dy)))),
    ];
}
