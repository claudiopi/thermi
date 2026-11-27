using System.Drawing;
using Thermi.App.Painting;
using Thermi.Core.Look;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Window;

internal sealed class WindowPainter : IOverlayPainter
{
    private readonly Painter _painter = new();

    public ((float Scale, (int Width, int Height)? Most, OverlayLook Look, string Items) Measured, float Scale)? Fitted { get; set; }

    public Size Measure(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale)
    {
        var (width, height) = _painter.Measure(items, look, scale);
        return new Size(width, height);
    }

    public PaintedPicture Picture(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale)
    {
        using var picture = _painter.Paint(items, look, scale);
        return new PaintedPicture(picture.Png(), picture.Width, picture.Height);
    }

    public PaintedPicture Stopped(string text, OverlayLook look, float scale, IReadOnlyList<OverlayItem> beside)
    {
        using var picture = _painter.PaintStopped(text, look, scale, beside);
        return new PaintedPicture(picture.Png(), picture.Width, picture.Height);
    }

    public void Dispose() => _painter.Dispose();
}
