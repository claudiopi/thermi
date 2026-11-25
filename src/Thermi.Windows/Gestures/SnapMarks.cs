using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Thermi.Windows.Painting;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Gestures;

/// <summary>
/// While an overlay or a pill is dragged, the spots it can snap to: each its own outline where it would go, on the
/// taskbars beside the notification area and at their start, and in the corners of every monitor, the one it would drop
/// onto filled. They let every click through, and sit under what is dragged.
/// </summary>
internal sealed class SnapMarks : IDisposable
{
    private const int Fill = 40;
    private const int LitFill = 120;
    private const int Border = 2;

    private readonly List<(SnapConfig Snap, Position At, HWND Window)> _marks = [];
    private readonly Size _size;
    private readonly float _scale;
    private SnapConfig? _lit;

    public SnapMarks(IReadOnlyList<Screen> screens, Orientation orientation, Size size, float scale, HWND dragged)
    {
        (_size, _scale) = (size, scale);
        foreach (var (snap, at) in Snaps.Marks(screens, orientation, size.Width, size.Height))
        {
            var window = TopLevelWindow.Create(OverlayWindow.ClassName, OverlayWindow.ExStyle | WINDOW_EX_STYLE.WS_EX_TRANSPARENT, WINDOW_STYLE.WS_POPUP);
            _marks.Add((snap, at, window));
            Paint(window, at, lit: false);
            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNA);
        }

        PInvoke.SetWindowPos(
            dragged,
            HWND.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>Fills the mark the overlay would drop onto, or none.</summary>
    public void Light(SnapConfig? snap)
    {
        if (snap == _lit)
        {
            return;
        }

        foreach (var (each, at, window) in _marks.Where(mark => mark.Snap == snap || mark.Snap == _lit))
        {
            Paint(window, at, each == snap);
        }

        _lit = snap;
    }

    public void Dispose()
    {
        foreach (var (_, _, window) in _marks)
        {
            PInvoke.DestroyWindow(window);
        }

        _marks.Clear();
    }

    // White inside a dark line, so a mark shows on any wallpaper and taskbar.
    private void Paint(HWND window, Position at, bool lit)
    {
        using var bitmap = new Bitmap(_size.Width, _size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var line = Border * _scale;
            var box = new RectangleF(line, line, _size.Width - (2 * line), _size.Height - (2 * line));
            using var path = new GraphicsPath();
            var diameter = Math.Min(box.Height, box.Width);
            path.AddArc(box.Left, box.Top, diameter, diameter, 90, 180);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 180);
            path.CloseFigure();
            using var fill = new SolidBrush(Color.FromArgb(lit ? LitFill : Fill, Color.White));
            using var dark = new Pen(Color.FromArgb(160, Color.Black), line * 2);
            using var light = new Pen(Color.White, line);
            g.FillPath(fill, path);
            g.DrawPath(dark, path);
            g.DrawPath(light, path);
        }

        LayeredPicture.ShowOnce(window, bitmap, at);
    }
}
