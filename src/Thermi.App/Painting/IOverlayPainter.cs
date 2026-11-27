using System.Drawing;
using Thermi.Core.Look;

namespace Thermi.App.Painting;

/// <summary>
/// An overlay's painter, whatever draws it on its OS, as what every host shares needs it: the room the items take, and
/// their picture as "Copy as image" and the window's previews show it. Each OS's own paints alike from Core's layout and
/// look, so the window answers the same on each.
/// </summary>
internal interface IOverlayPainter : IDisposable
{
    /// <summary>The last scale <see cref="PainterFit.Fitting"/> found, and what it measured.</summary>
    ((float Scale, (int Width, int Height)? Most, OverlayLook Look, string Items) Measured, float Scale)? Fitted { get; set; }

    /// <summary>
    /// The size the items paint at, without painting them: each value keeps the room of its widest form, so this is the room
    /// the overlay keeps however its numbers change.
    /// </summary>
    Size Measure(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale);

    PaintedPicture Picture(IReadOnlyList<OverlayItem> items, OverlayLook look, float scale);

    /// <summary>
    /// The one pill that stands for every item the sensor service reads while it is stopped, thermi's mark and
    /// <paramref name="text"/>, then the items <paramref name="beside"/> it, which never wait on the service, as a PNG.
    /// </summary>
    PaintedPicture Stopped(string text, OverlayLook look, float scale, IReadOnlyList<OverlayItem> beside);
}

internal sealed record PaintedPicture(byte[] Png, int Width, int Height);
