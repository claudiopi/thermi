using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;

namespace Thermi.Windows.Win32;

/// <summary>Windows' own dialogs to pick a file to open or to save, of one type, over the window that asks.</summary>
internal static class FileDialogs
{
    // Long paths fit; the dialog says when a path is longer.
    private const int MaxPath = 32 * 1024;

    /// <summary>The file picked to open, or null when the dialog was cancelled.</summary>
    public static string? Open(HWND owner, string kind, string extension) =>
        Pick(
            owner,
            kind,
            extension,
            "",
            OPEN_FILENAME_FLAGS.OFN_FILEMUSTEXIST | OPEN_FILENAME_FLAGS.OFN_PATHMUSTEXIST | OPEN_FILENAME_FLAGS.OFN_NOCHANGEDIR,
            save: false);

    /// <summary>The file to save to, starting as <paramref name="name"/>, or null when the dialog was cancelled.</summary>
    public static string? Save(HWND owner, string kind, string extension, string name) =>
        Pick(
            owner,
            kind,
            extension,
            name,
            OPEN_FILENAME_FLAGS.OFN_OVERWRITEPROMPT | OPEN_FILENAME_FLAGS.OFN_PATHMUSTEXIST | OPEN_FILENAME_FLAGS.OFN_NOCHANGEDIR,
            save: true);

    // The filter is the kind's name and its pattern, each ended by a null, and the list by another.
    private static unsafe string? Pick(HWND owner, string kind, string extension, string name, OPEN_FILENAME_FLAGS flags, bool save)
    {
        var file = new char[MaxPath];
        name.AsSpan(0, Math.Min(name.Length, MaxPath - 1)).CopyTo(file);
        var filter = $"{kind} (*{extension})\0*{extension}\0\0";
        var defaultExtension = extension.TrimStart('.') + "\0";
        fixed (char* path = file, filters = filter, ext = defaultExtension)
        {
            var dialog = new OPENFILENAMEW
            {
                lStructSize = (uint)Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = owner,
                lpstrFilter = filters,
                lpstrFile = path,
                nMaxFile = MaxPath,
                lpstrDefExt = ext,
                Flags = flags,
            };
            var picked = save ? PInvoke.GetSaveFileName(ref dialog) : PInvoke.GetOpenFileName(ref dialog);
            return picked ? new string(path) : null;
        }
    }
}
