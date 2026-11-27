using System.Drawing;
using Thermi.App.Gestures;
using Thermi.App.Logging;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;
using Thermi.Linux.Painting;
using Screen = Thermi.Core.Placement.Screen;

namespace Thermi.Linux.Overlay;

/// <summary>
/// What a Linux desktop shows of an overlay's drag: the pill a hold lifted, carried as a picture of its own above the
/// snap marks, and the marks. No surface lets thermi change the pointer's shape, so a refused drop shows as its marks.
/// </summary>
internal sealed class DragShow(OverlayParts parts, ShellScreens stage, Func<Picture>? carried) : IDragShow
{
    /// <summary>The id of the lifted pill's picture, past every overlay's and mark's.</summary>
    public const uint Ghost = uint.MaxValue - 1;

    private bool _lifted;

    public Size Lift(Position at)
    {
        if (carried is null)
        {
            return Size.Empty;
        }

        using var picture = carried();
        using var pixels = picture.Bytes();
        var (x, y) = stage.Stage(at);
        var shown = parts.Surfaces.Show(Ghost, new ShellPicture(pixels, picture.Width, picture.Height, picture.Stride, stage.Scale, []), x, y);
        _lifted = true;
        parts.Trace(TraceEvents.Lifted, parts.Call(shown));
        return new Size(picture.Width, picture.Height);
    }

    public void Carry(Position at)
    {
        if (_lifted)
        {
            var (x, y) = stage.Stage(at);
            _ = parts.Call(parts.Surfaces.Move(Ghost, x, y));
        }
    }

    public IDragMarks Marks(IReadOnlyList<Screen> screens, Orientation orientation, Size size, bool lifted) =>
        new SnapMarks(parts, stage, orientation, (size.Width, size.Height));

    public void Refuse(bool refused)
    {
        // The marks say where a drop goes, and a refused one lights none.
    }

    // Clutter's, GDK's and X11's Alt alike: the first modifier past Shift, Lock and Control.
    public bool AltHeld() => parts.Cursor is { } cursor && (cursor.Keys & (1 << 3)) != 0;

    public void Dispose()
    {
        if (_lifted)
        {
            _ = parts.Call(parts.Surfaces.Hide(Ghost));
        }
    }
}
