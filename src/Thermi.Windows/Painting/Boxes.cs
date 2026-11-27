using System.Drawing;
using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.Windows.Painting;

/// <summary>Core's boxes as GDI+'s rectangles and points, and back.</summary>
internal static class Boxes
{
    public static RectangleF Rect(this PillBox box) => new((float)box.Left, (float)box.Top, (float)box.Width, (float)box.Height);

    public static Rectangle Rect(this PixelRect box) => Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom);

    public static PillBox Box(this RectangleF rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    public static PointF[] Points(this IEnumerable<(double X, double Y)> points) => [.. points.Select(point => new PointF((float)point.X, (float)point.Y))];
}
