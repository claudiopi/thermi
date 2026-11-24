using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;

namespace Thermi.Windows;

/// <summary>The text Windows' clipboard holds, read without taking it from the app that put it there.</summary>
internal static class ClipboardText
{
    /// <summary>The clipboard's text, or null when it holds none or another app keeps it open.</summary>
    public static unsafe string? Read(HWND owner)
    {
        if (!PInvoke.OpenClipboard(owner))
        {
            return null;
        }

        try
        {
            var data = PInvoke.GetClipboardData((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT);
            if (data.IsNull)
            {
                return null;
            }

            var memory = new HGLOBAL(data.Value);
            var text = PInvoke.GlobalLock(memory);
            if (text is null)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni((nint)text);
            }
            finally
            {
                PInvoke.GlobalUnlock(memory);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }
}
