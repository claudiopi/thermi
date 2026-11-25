using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Thermi.Commands;
using Thermi.Core.Settings;
using Thermi.Tray;
using Thermi.Updates;
using Thermi.Windows;
using Thermi.Windows.Clipboard;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Overlay;

/// <summary>
/// What the host reaches outside its process, which tests replace with their own: the window's single-instance mutex,
/// the executable's path the tray icons' ids are tied to and the window starts from, the notification area, opening the
/// window, the cursor, starting the sensors, the clipboard and Task Manager.
/// </summary>
internal sealed record HostEnvironment(string WindowMutex, string ExePath, NotifyShell Shell)
{
    /// <summary>The window's single-instance mutex, which exists while the window is open.</summary>
    public const string WindowMutexName = @"Local\thermi-window";

    /// <summary>The overlay's single-instance mutex, which exists while an overlay runs in this session.</summary>
    public const string OverlayMutexName = @"Local\thermi-overlay";

    public static HostEnvironment Default { get; } = new(
        WindowMutexName,
        ThermiPaths.Launcher,
        (message, in data) => PInvoke.Shell_NotifyIcon(message, data));

    /// <summary>The executable the window starts from: this version's own, so the window runs the overlay's version.</summary>
    public string WindowExe { get; init; } = ThermiPaths.Own;

    /// <summary>Opens the window on an overlay, by its place; null starts it as <see cref="StartWindow"/> does.</summary>
    public Action<int>? OpenSettings { get; init; }

    public Func<Position> Cursor { get; init; } = Desktop.Cursor;

    /// <summary>
    /// "Start sensors", behind its UAC prompt: the elevated mode's exit code, or null when the prompt was refused.
    /// </summary>
    public Func<Task<int?>> StartSensors { get; init; } = Commands.StartSensors.AskAsync;

    /// <summary>Puts an overlay's picture on the clipboard, for the window that owns it; tests keep it, and never touch the real one.</summary>
    public Action<HWND, Bitmap> CopyImage { get; init; } = ClipboardImage.Write;

    /// <summary>
    /// Opens Windows' Task Manager, which shows what keeps the PC busy, as the user: started directly rather than through
    /// the shell, whose reading of its manifest would raise it to administrator without asking.
    /// </summary>
    public Action TaskManager { get; init; } = () =>
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskmgr.exe")) { UseShellExecute = false })?.Dispose();

    // The window holds its mutex while it is open. The overlay only looks for it, never holds it, so a window that closes
    // or crashes takes it away. Any program in the session can make one of the name too: the overlay then believes the
    // window open, which only changes how it looks and rests its hotkeys, and such a program could already do worse as
    // the user.
    public bool WindowOpen() => NamedMutex.Exists(WindowMutex);

    /// <summary>
    /// Opens the window on an overlay, by its place, and an item to select, as <c>thermi settings --overlay n --item id</c>:
    /// a window already open comes to the front on it instead. The click that asked still lets this process give the front
    /// away, and the new process needs it.
    /// </summary>
    public void StartWindow(int overlay, string? item, bool trace)
    {
        PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
        string[] arguments = ["settings", ThermiCommands.OverlayOption, (overlay + 1).ToString(CultureInfo.InvariantCulture)];
        Program.Start(WindowExe, trace, item is null ? arguments : [.. arguments, ThermiCommands.ItemOption, item]);
    }
}
