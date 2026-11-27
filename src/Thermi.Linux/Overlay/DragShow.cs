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
/// snap marks, drawn at the scale of the monitor its corner is over, as the overlays are, and the marks. No surface lets
/// thermi change the pointer's shape, so a refused drop shows as its marks.
/// </summary>
internal sealed class DragShow(OverlayParts parts, ShellScreens stage, Func<double, Picture>? carried) : IDragShow
{
    /// <summary>The id of the lifted pill's picture, past every overlay's and mark's.</summary>
    public const uint Ghost = uint.MaxValue - 1;

    private bool _lifted;
    private double _scale;

    public Size Lift(Position at)
    {
        if (carried is null)
        {
            return Size.Empty;
        }

        var (size, shown) = Show(at);
        _lifted = true;
        parts.Trace(TraceEvents.Lifted, parts.Call(shown));
        return size;
    }

    public void Carry(Position at)
    {
        if (!_lifted)
        {
            return;
        }

        if (Math.Abs(ScaleAt(at) - _scale) > 1e-6)
        {
            _ = parts.Call(Show(at).Shown);
            return;
        }

        var (x, y) = stage.Stage(at);
        _ = parts.Call(parts.Surfaces.Move(Ghost, x, y));
    }

    // The pill drawn for the monitor at its corner, its size in thermi's pixels.
    private (Size Size, Task Shown) Show(Position at)
    {
        _scale = ScaleAt(at);
        using var picture = carried!(_scale);
        using var pixels = picture.Bytes();
        var (x, y) = stage.Stage(at);
        var shown = parts.Surfaces.Show(Ghost, new ShellPicture(pixels, picture.Width, picture.Height, picture.Stride, _scale, []), x, y);
        var toThermi = stage.Scale / _scale;
        return (new Size((int)Math.Round(picture.Width * toThermi), (int)Math.Round(picture.Height * toThermi)), shown);
    }

    private double ScaleAt(Position at) => stage.Screens.FirstOrDefault(screen => screen.Bounds.Contains(at))?.Scale ?? stage.Scale;

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
