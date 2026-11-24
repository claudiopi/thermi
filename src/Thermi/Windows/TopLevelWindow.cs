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
}
