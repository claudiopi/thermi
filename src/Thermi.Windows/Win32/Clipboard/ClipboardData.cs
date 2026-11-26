using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace Thermi.Windows.Win32.Clipboard;

/// <summary>What thermi leaves on Windows' clipboard: a picture's formats, or a text.</summary>
internal static class ClipboardData
{
    /// <summary>Takes the clipboard for <paramref name="owner"/>, empties it and leaves what <paramref name="put"/> puts, unless another holds it.</summary>
    public static void Write(HWND owner, Action put)
    {
        if (!PInvoke.OpenClipboard(owner))
        {
            return;
        }

        try
        {
            PInvoke.EmptyClipboard();
            put();
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>Leaves <paramref name="text"/> on the clipboard as Unicode text, which every app pastes.</summary>
    public static void WriteText(HWND owner, string text) =>
        Write(owner, () => Put((uint)CLIPBOARD_FORMAT.CF_UNICODETEXT, Encoding.Unicode.GetBytes(text + '\0')));

    // Once set, the memory is the clipboard's; only memory it refused is freed here.
    public static unsafe void Put(uint format, byte[] bytes)
    {
        var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)bytes.Length);
        if (memory.IsNull)
        {
            return;
        }

        var at = PInvoke.GlobalLock(memory);
        if (at is not null)
        {
            Marshal.Copy(bytes, 0, (nint)at, bytes.Length);
            PInvoke.GlobalUnlock(memory);
        }

        if (at is null || PInvoke.SetClipboardData(format, new HANDLE(memory.Value)).IsNull)
        {
            PInvoke.GlobalFree(memory);
        }
    }
}
