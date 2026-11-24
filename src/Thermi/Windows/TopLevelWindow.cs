using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows;

/// <summary>
/// Makes a top-level window of a class <see cref="HiddenWindow.Register"/> registered in thermi's module, titled thermi:
/// shown or not as its styles and its owner say, as the overlays, the drag's ghost and the settings window are.
/// </summary>
internal static class TopLevelWindow
{
    public static unsafe HWND Create(string className, WINDOW_EX_STYLE style, WINDOW_STYLE kind)
    {
        fixed (char* name = className)
        fixed (char* title = "thermi")
        {
            return PInvoke.CreateWindowEx(style, name, title, kind, 0, 0, 0, 0, HWND.Null, HMENU.Null, HiddenWindow.Instance, null);
        }
    }

    /// <summary>Lets every click through the window, or none: a layered window that is also transparent lets them all through.</summary>
    public static void ClickThrough(HWND window, bool on)
    {
        const nint Transparent = (nint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        var style = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var wanted = on ? style | Transparent : style & ~Transparent;
        if (wanted != style)
        {
            PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, wanted);
        }
    }
}
