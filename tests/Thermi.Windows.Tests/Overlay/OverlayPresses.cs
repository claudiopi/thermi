using System.Drawing;
using System.Drawing.Imaging;
using Thermi.App.SettingsFile;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Gestures;
using Thermi.Windows.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Storage.Xps;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>
/// A press on an overlay as Windows brings it, on a desktop of the test's own: the mouse's messages posted to the
/// overlay's window, with the cursor where the test puts it, never the user's.
/// </summary>
public abstract class OverlayPresses
{
    // Far past any drag distance, and still on the primary monitor from its taskbar's right end.
    private protected static readonly Position By = new(-300, -200);

    private protected static readonly Position Nowhere = new(int.MinValue, int.MinValue);

    private protected SettingsFolder Folder { get; } = new();
    private readonly FakeTray _tray = new();
    private readonly string _windowMutex = HostRun.WindowMutex();
    private protected Position Pointer { get; set; } = new(0, 0);

    // In front of the first overlay's first pill, or of the overlay of one pill, or, for the overlay of many held in its
    // middle, 20 px left of the overlay of one pill, the overlay of many then over all of it; or on the CPU power's value.
    private protected static Position DroppedAt(OverlayHost host, string drag, Position pressed)
    {
        PInvoke.GetWindowRect(host.Overlays[drag is "lifted" or "over" ? 1 : 0].Hwnd, out var rect);
        var middle = (rect.top + rect.bottom) / 2;
        return drag switch
        {
            "back" or "off" or "value" or "label" => new Position(pressed.X + By.X, pressed.Y + By.Y),
            "within" => EndOf(host.Overlays[0], Metrics.CpuPower.Id, label: false),
            "over" => new Position(rect.left - 20, middle),
            _ => new Position(rect.left + 3, middle),
        };
    }

    // The cursor put at a point and the mouse's message sent, then the paints it asks for let out.
    private protected void MoveTo(OverlayHost host, OverlayWindow overlay, Position to, uint message = PInvoke.WM_MOUSEMOVE)
    {
        Pointer = to;
        Post(overlay.Hwnd, message);
        for (var paints = 0; paints < 3; paints++)
        {
            Folder.Clock.Advance(Settings.Interval);
            HostRun.Pump(host);
        }
    }

    // The press held still until it lifts, as the hold's timer wakes the overlay.
    private protected void Hold(OverlayHost host, OverlayWindow held)
    {
        Folder.Clock.Advance(DesktopGestures.Hold);
        PInvoke.PostMessage(held.Hwnd, PInvoke.WM_TIMER, OverlayGestures.HoldTimer, 0);
        HostRun.Pump(host);
    }

    // On the overlay's middle row, 2 px into an item: from its start, on the label of the pill it starts, or from its end,
    // on its value.
    private protected static Position EndOf(OverlayWindow overlay, string item, bool label)
    {
        PInvoke.GetWindowRect(overlay.Hwnd, out var rect);
        var y = (rect.top + rect.bottom) / 2;
        var on = Enumerable.Range(rect.left, rect.right - rect.left).Where(x => overlay.ItemAt(new Position(x, y)) == item).ToList();
        return new Position(label ? on[0] + 2 : on[^1] - 2, y);
    }

    private protected static Position Middle(HWND window) =>
        PInvoke.GetWindowRect(window, out var rect) ? new Position((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2) : new Position(0, 0);

    /// <summary>The middle of the run of the overlay's middle row the item's pill is drawn on.</summary>
    internal static Position PillOf(OverlayWindow overlay, string item)
    {
        PInvoke.GetWindowRect(overlay.Hwnd, out var rect);
        var y = (rect.top + rect.bottom) / 2;
        var on = Enumerable.Range(rect.left, rect.right - rect.left).Where(x => overlay.ItemAt(new Position(x, y)) == item).ToList();
        return new Position((on[0] + on[^1]) / 2, y);
    }

    // Esc comes as the overlay's hotkey, with the mouse still; or Esc and Alt are down as the move or the drop is sent, as
    // the window's thread reads them.
    private protected static void End(OverlayHost host, HWND overlay, string end)
    {
        switch (end)
        {
            case "esc":
                PInvoke.PostMessage(overlay, PInvoke.WM_HOTKEY, OverlayGestures.EscapeKey, 0);
                HostRun.Pump(host);
                break;
            case "esc-moved":
                Holding(0x1B, () => Post(overlay, PInvoke.WM_MOUSEMOVE), host);
                break;
            case "lost":
                using (var other = new HiddenTestWindow("thermi-test-" + Guid.NewGuid().ToString("N")))
                {
                    PInvoke.SetCapture(other.Hwnd);
                    HostRun.Pump(host);
                    PInvoke.ReleaseCapture();
                }

                break;
            case "alt":
                Holding(0x12, () => Post(overlay, PInvoke.WM_LBUTTONUP), host);
                return;
            default:
                break;
        }

        Post(overlay, PInvoke.WM_LBUTTONUP);
        HostRun.Pump(host);
    }

    private protected static void Holding(byte key, Action send, OverlayHost host)
    {
        var keys = new byte[256];
        keys[key] = 0x80;
        TestPInvoke.SetKeyboardState(keys);
        send();
        HostRun.Pump(host);
        TestPInvoke.SetKeyboardState(new byte[256]);
    }

    // The windows the test's desktop shows, which only the host's process has windows on.
    private protected static List<HWND> Shown()
    {
        var shown = new List<HWND>();
        TestPInvoke.EnumWindows(
            (hwnd, _) =>
            {
                if (TestPInvoke.IsWindowVisible(hwnd))
                {
                    shown.Add(hwnd);
                }

                return true;
            },
            0);
        return shown;
    }

    // PW_RENDERFULLCONTENT, which Windows' metadata leaves out of the flags.
    private const PRINT_WINDOW_FLAGS RenderFullContent = (PRINT_WINDOW_FLAGS)2;

    // What the window shows, as Windows composes it: a layered window's picture, which no other call hands back.
    private protected static Bitmap Picture(HWND window)
    {
        PInvoke.GetWindowRect(window, out var rect);
        var picture = new Bitmap(rect.right - rect.left, rect.bottom - rect.top, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(picture);
        var dc = graphics.GetHdc();
        try
        {
            TestPInvoke.PrintWindow(window, new HDC(dc), RenderFullContent);
        }
        finally
        {
            graphics.ReleaseHdc(dc);
        }

        return picture;
    }

    private protected static Position Corner(HWND window) =>
        PInvoke.GetWindowRect(window, out var rect) ? new Position(rect.left, rect.top) : new Position(0, 0);

    private protected static void Post(HWND window, uint message) => PInvoke.PostMessage(window, message, 0, 0);

    private protected void Run(Settings settings, Action<OverlayHost> body, Action<int>? openSettings = null) =>
        HostRun.Run(settings, Folder, _tray.Environment(_windowMutex, openSettings) with { Cursor = () => Pointer }, body);

    // A press in the overlay's middle, the cursor nudged by a pixel, within any drag distance, then moved by By, or by
    // `by`, the button still down; gives where the overlay is after each move.
    private protected (Position Nudged, Position Dragged) Drag(OverlayHost host, HWND overlay) => DragBy(host, overlay, By);

    private protected (Position Nudged, Position Dragged) DragBy(OverlayHost host, HWND overlay, Position by)
    {
        PInvoke.GetWindowRect(overlay, out var rect);
        var pressed = new Position((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
        Pointer = pressed;
        Post(overlay, PInvoke.WM_LBUTTONDOWN);
        HostRun.Pump(host);
        Pointer = new Position(pressed.X + 1, pressed.Y + 1);
        Post(overlay, PInvoke.WM_MOUSEMOVE);
        HostRun.Pump(host);
        var nudged = Corner(overlay);
        Pointer = new Position(pressed.X + by.X, pressed.Y + by.Y);
        Post(overlay, PInvoke.WM_MOUSEMOVE);
        HostRun.Pump(host);
        return (nudged, Corner(overlay));
    }
}
