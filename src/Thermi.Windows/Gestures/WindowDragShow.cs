using System.Drawing;
using Thermi.App.Gestures;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Thermi.Windows.Painting;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Gestures;

/// <summary>
/// A drag as Windows shows it: a lifted pill carried as a layered window of its own that lets every click through, the snap
/// marks under what is dragged, and the cursor that says a drop does nothing.
/// </summary>
internal sealed class WindowDragShow(HWND window, float scale, Func<Bitmap>? picture) : IDragShow
{
    private HWND _ghost;

    public Size Lift(Position at)
    {
        _ghost = TopLevelWindow.Create(OverlayWindow.ClassName, OverlayWindow.ExStyle | WINDOW_EX_STYLE.WS_EX_TRANSPARENT, WINDOW_STYLE.WS_POPUP);
        using var bitmap = picture!();
        LayeredPicture.ShowOnce(_ghost, bitmap, at);
        PInvoke.ShowWindow(_ghost, SHOW_WINDOW_CMD.SW_SHOWNA);
        return bitmap.Size;
    }

    public void Carry(Position at) =>
        PInvoke.SetWindowPos(
            _ghost,
            HWND.Null,
            at.X,
            at.Y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    public IDragMarks Marks(IReadOnlyList<Screen> screens, Orientation orientation, Size size, bool lifted) =>
        new SnapMarks(screens, orientation, size, scale, lifted ? _ghost : window);

    public void Refuse(bool refused) => PInvoke.SetCursor(PInvoke.LoadCursor(HINSTANCE.Null, refused ? PInvoke.IDC_NO : PInvoke.IDC_ARROW));

    /// <summary>Whether Alt was down as the message being handled was sent.</summary>
    public bool AltHeld() => PInvoke.GetKeyState((int)VIRTUAL_KEY.VK_MENU) < 0;

    public void Dispose()
    {
        if (!_ghost.IsNull)
        {
            PInvoke.DestroyWindow(_ghost);
        }
    }

    /// <summary>Each overlay's window on the screen, null for one not shown.</summary>
    public static IReadOnlyList<PixelRect?> Boxes(IReadOnlyList<OverlayWindow> overlays) =>
        [.. overlays.Select(overlay => PInvoke.IsWindowVisible(overlay.Hwnd) && PInvoke.GetWindowRect(overlay.Hwnd, out var rect)
            ? new PixelRect(rect.left, rect.top, rect.right, rect.bottom)
            : (PixelRect?)null)];
}
