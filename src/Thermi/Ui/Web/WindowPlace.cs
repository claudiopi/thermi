using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Logging;
using Thermi.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Ui.Web;

/// <summary>
/// Where the window opens: the first time from Core's plain geometry, off thermi's free overlays, and after that where it
/// was, kept for each setup of monitors in <c>window.json</c> beside the log. Core's sizes are at 100 %, so they are
/// scaled by the monitor's scale; a place that cannot be read or kept is logged and the first one used.
/// </summary>
internal sealed class WindowPlace(string folder, ILogger log)
{
    private readonly string _file = Path.Combine(folder, "window.json");

    public bool Maximized { get; private set; }

    public void Restore(HWND window, ThermiConfig config, IReadOnlyList<Screen> screens)
    {
        var place = Remembered(screens) ?? First(config, screens);
        PInvoke.SetWindowPos(
            window,
            HWND.Null,
            place.Left,
            place.Top,
            place.Width,
            place.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>The smallest the window may be on the monitor it is on, or that monitor's work area when smaller.</summary>
    public static unsafe void Limit(HWND window, LPARAM lParam, IReadOnlyList<Screen> screens)
    {
        PInvoke.GetWindowRect(window, out var frame);
        var screen = screens.FirstOrDefault(each => each.Bounds.Overlaps(new PixelRect(frame.left, frame.top, frame.right, frame.bottom)))
            ?? screens.FirstOrDefault(each => each.Primary);
        if (screen is null)
        {
            return;
        }

        var (width, height) = WindowRoom.Minimum(Scaled(screen.Work, 1 / screen.Scale));
        var limits = (MINMAXINFO*)(nint)lParam;
        limits->ptMinTrackSize = new System.Drawing.Point((int)(width * screen.Scale), (int)(height * screen.Scale));
    }

    /// <summary>Keeps the window's normal place, and whether it is maximized, under the monitors it is on now.</summary>
    public void Keep(HWND window, IReadOnlyList<Screen> screens)
    {
        var placement = new WINDOWPLACEMENT { length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!PInvoke.GetWindowPlacement(window, ref placement))
        {
            return;
        }

        var normal = placement.rcNormalPosition;
        try
        {
            var all = Read() ?? [];
            all[WindowRoom.Setup(screens)] = new JsonArray(
                normal.left,
                normal.top,
                normal.right,
                normal.bottom,
                placement.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, all.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.WindowPlaceFailed(ex);
        }
    }

    private static PixelRect Scaled(PixelRect rect, double scale) =>
        new(
            (int)Math.Round(rect.Left * scale),
            (int)Math.Round(rect.Top * scale),
            (int)Math.Round(rect.Right * scale),
            (int)Math.Round(rect.Bottom * scale));

    // Core's geometry at 100 %, on the first overlay's monitor's scale.
    private static PixelRect First(ThermiConfig config, IReadOnlyList<Screen> screens)
    {
        var scale = Snaps.ScreenOf(config.Overlays[0], screens)?.Scale ?? 1;
        var at100 = screens.Select(screen => screen with { Bounds = Scaled(screen.Bounds, 1 / scale), Work = Scaled(screen.Work, 1 / scale) }).ToList();
        var free = FreeOverlays(screens).Select(overlay => Scaled(overlay, 1 / scale)).ToList();
        return Scaled(WindowRoom.First(config, at100, free), scale);
    }

    private static List<PixelRect> FreeOverlays(IReadOnlyList<Screen> screens)
    {
        var free = new List<PixelRect>();
        for (
            var overlay = PInvoke.FindWindowEx(HWND.Null, HWND.Null, OverlayWindow.ClassName, null);
            !overlay.IsNull;
            overlay = PInvoke.FindWindowEx(HWND.Null, overlay, OverlayWindow.ClassName, null))
        {
            if (PInvoke.IsWindowVisible(overlay) && PInvoke.GetWindowRect(overlay, out var rect))
            {
                var place = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
                if (!screens.Any(screen => screen.Taskbar is { } taskbar && taskbar.Overlaps(place)))
                {
                    free.Add(place);
                }
            }
        }

        return free;
    }

    private PixelRect? Remembered(IReadOnlyList<Screen> screens)
    {
        try
        {
            if (Read()?[WindowRoom.Setup(screens)] is JsonArray { Count: 5 } kept)
            {
                Maximized = kept[4]!.GetValue<bool>();
                return new PixelRect(kept[0]!.GetValue<int>(), kept[1]!.GetValue<int>(), kept[2]!.GetValue<int>(), kept[3]!.GetValue<int>());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            log.WindowPlaceFailed(ex);
        }

        return null;
    }

    private JsonObject? Read() => File.Exists(_file) ? JsonNode.Parse(File.ReadAllText(_file)) as JsonObject : null;
}
