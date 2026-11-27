using Thermi.Core.Look;

namespace Thermi.Core.Painting;

/// <summary>A pixel of the hand-placed mark: where, from its square's top left, and its color, or null for the ink's.</summary>
public readonly record struct MarkCell(int X, int Y, Rgb? Color);

/// <summary>
/// thermi's mark placed by hand, pixel by pixel, at 16 and 24 px, where the vector would blur into a smudge: the body
/// row by row, the eyes as cells cut out of it, and the line below.
/// </summary>
public sealed class MarkPixels
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
        [
            (7, 8, 15), (8, 6, 17), (9, 4, 19), (10, 3, 20), (11, 3, 20), (12, 2, 21),
            (13, 2, 21), (14, 2, 21), (15, 2, 21), (16, 2, 21), (17, 2, 21), (18, 2, 21),
        ],
        (20, 4, 19),
        Eyes((10, 10), (16, 10), ".##.", "####", "####", ".##."),
        Eyes((10, 10), (16, 10), ".##.", "####", "####", "####", ".##."),
        Eyes((10, 12), (16, 12), "#..#", ".##."));

    private readonly (int Y, int From, int To)[] _body;
    private readonly Dictionary<MarkEyes, HashSet<(int X, int Y)>> _eyes;

    private MarkPixels(
        int size,
        (int Y, int From, int To)[] body,
        (int Y, int From, int To) line,
        HashSet<(int X, int Y)> open,
        HashSet<(int X, int Y)> wide,
        HashSet<(int X, int Y)> closed)
    {
        (Size, _body, Line) = (size, body, line);
        _eyes = new() { [MarkEyes.Open] = open, [MarkEyes.Wide] = wide, [MarkEyes.Closed] = closed };
    }

    /// <summary>The side of the square the cells are placed in.</summary>
    public int Size { get; }

    /// <summary>The line under the body, a row of the ink's.</summary>
    public (int Y, int From, int To) Line { get; }

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

    /// <summary>The body's cells in <paramref name="look"/>: its gradient with the eyes in their ink, or, one-color, the ink with the eyes cut out.</summary>
    public IEnumerable<MarkCell> Body(MarkLook look)
    {
        var eyes = _eyes[look.Eyes];
        foreach (var (y, from, to) in _body)
        {
            for (var x = from; x <= to; x++)
            {
                var eye = eyes.Contains((x, y));
                if (look.Stops is null && eye)
                {
                    continue;
                }

                yield return new MarkCell(x, y, ColorOf(look, eye, x));
            }
        }
    }

    private Rgb? ColorOf(MarkLook look, bool eye, int x)
    {
        if (look.Stops is null)
        {
            return null;
        }

        return eye ? MarkLook.EyeInk : look.At((x + 0.5 - 1) / (Size - 1));
    }

    private static HashSet<(int X, int Y)> Eyes((int X, int Y) left, (int X, int Y) right, params string[] pattern) =>
    [
        .. new[] { left, right }.SelectMany(corner => pattern.SelectMany((row, dy) => row
            .Select((cell, dx) => (cell, dx))
            .Where(each => each.cell == '#')
            .Select(each => (corner.X + each.dx, corner.Y + dy)))),
    ];
}
