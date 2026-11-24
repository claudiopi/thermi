using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Gestures;
using Thermi.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>
/// A press on an overlay as Windows brings it, on a desktop of the test's own: the mouse's messages posted to the
/// overlay's window, with the cursor where the test puts it, never the user's.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class OverlayDragTests : IDisposable
{
    // Far past any drag distance, and still on the primary monitor from its taskbar's right end.
    private static readonly Position By = new(-300, -200);

    private static readonly Position Nowhere = new(int.MinValue, int.MinValue);

    private readonly SettingsFolder _folder = new();
    private readonly FakeTray _tray = new();
    private readonly string _windowMutex = HostRun.WindowMutex();
    private Position _cursor = new(0, 0);

    public void Dispose() => _folder.Dispose();

    // The overlay of several pills, pressed on one of them, follows the cursor whole only past the drag distance, stays
    // where it was let go, and the file gets that spot, off its snap.
    [Fact]
    public async Task ADragMovesTheOverlayAndWritesWhereItWasDropped()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig());
        var (start, nudged, during, after, written) = (Nowhere, Nowhere, Nowhere, Nowhere, false);

        Run(settings, host =>
        {
            var overlay = host.Overlays[0].Hwnd;
            start = Corner(overlay);
            (nudged, during) = Drag(host, overlay);
            Post(overlay, PInvoke.WM_LBUTTONUP);
            written = HostRun.Until(host, _folder.Clock, () => _folder.Written()?.Contains("\"position\"", StringComparison.Ordinal) == true);
            HostRun.Tick(host);
            after = Corner(overlay);
        });

        var dropped = new Position(start.X + By.X, start.Y + By.Y);
        Assert.True(written, "The drop was never written.");
        Assert.Equal((start, dropped, dropped), (nudged, during, after));
        Assert.Equal((dropped, null), (_folder.Read().Overlays[0].Position, _folder.Read().Overlays[0].Snap));
    }

    // A locked overlay takes no drag, and a double-click on it still opens the window, which its window class must let
    // Windows send.
    [Fact]
    public async Task ALockedOverlayStaysWhereItIs()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Overlays = [new OverlayConfig { Locked = true }] });
        var before = _folder.Text();
        var corners = new List<Position>();
        var opened = new List<int>();

        Run(settings, host =>
        {
            var overlay = host.Overlays[0].Hwnd;
            corners.Add(Corner(overlay));
            corners.Add(Drag(host, overlay).Dragged);
            Post(overlay, PInvoke.WM_LBUTTONUP);
            HostRun.Pump(host);
            HostRun.Tick(host);
            corners.Add(Corner(overlay));
            Post(overlay, PInvoke.WM_LBUTTONDBLCLK);
            HostRun.Pump(host);
        }, opened.Add);

        Assert.Single(corners.Distinct());
        Assert.Null(settings.Config.Overlays[0].Position);
        Assert.Equal(before, _folder.Text());
        Assert.Equal([0], opened);
        Assert.True(OverlayWindow.WindowClass(TestWindows.DefaultProcedure, default).style.HasFlag(WNDCLASS_STYLES.CS_DBLCLKS));
    }

    // A drag ends with nothing of it on screen but the overlay, its marks gone: let go near a corner's mark it snaps there,
    // and with Alt held it stays where it was let go; with Esc pressed, which the press holds as the overlay's hotkey so no
    // other app gets it, or Esc down as the mouse moves, or another window taking the mouse, as a menu or a dialog does, it
    // goes back to its snap and the button let go after moves nothing.
    [Theory]
    [InlineData("drop", "TopLeft")]
    [InlineData("alt", "free")]
    [InlineData("esc", "back")]
    [InlineData("esc-moved", "back")]
    [InlineData("lost", "back")]
    public async Task ADragEndsWithNothingOfItLeftOnScreen(string end, string lands)
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig());
        var before = _folder.Text();
        var (start, near, after, written) = (Nowhere, Nowhere, Nowhere, false);
        var (during, left) = (new List<HWND>(), new List<HWND>());
        HWND overlay = default;

        Run(settings, host =>
        {
            overlay = host.Overlays[0].Hwnd;
            PInvoke.GetWindowRect(overlay, out var rect);
            start = Corner(overlay);
            var topLeft = new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopLeft) };
            var corner = Snaps.Locate(topLeft, Desktop.Screens(), rect.right - rect.left, rect.bottom - rect.top)!;
            near = new Position(corner.X + 20, corner.Y + 20);
            DragBy(host, overlay, new Position(near.X - start.X, near.Y - start.Y));
            during.AddRange(Shown());
            End(host, overlay, end);
            written = HostRun.Until(host, _folder.Clock, () => _folder.Text() != before);
            HostRun.Tick(host);
            after = Corner(overlay);
            left.AddRange(Shown());
        });

        Assert.True(during.Count > 1, "No snap mark showed during the drag.");
        Assert.Equal([overlay], left);
        var file = _folder.Read().Overlays[0];
        Assert.Equal(
            lands switch
            {
                "back" => (false, start, (SnapSpot?)null, (Position?)null),
                "free" => (true, near, null, near),
                _ => (true, after, SnapSpot.TopLeft, null),
            },
            (written, after, file.Snap?.Spot, file.Position));
    }

    // A pill held still lifts, and dropped back on its own overlay in front of its first pill moves there; a press that left
    // the drag distance and came back moves the overlay whole however long it then holds; an overlay of one pill dragged onto
    // another shows whole, not faded, and joins it before the pill it is let go on. The overlays left, as their count, the
    // first overlay's first item and whether it is still snapped.
    [Theory]
    [InlineData("own", "1 gpu.load snapped")]
    [InlineData("back", "1 cpu.temperature free")]
    [InlineData("merge", "1 solo snapped")]
    public async Task APressLiftsOnlyHeldStillAndADropJoinsWhereItIsLetGo(string drag, string expected)
    {
        var solo = new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight), Items = [new ItemConfig { Id = "solo", Metric = Metrics.GpuTemperature.Id }] };
        var settings = await _folder.LoadedAsync(drag == "merge" ? new ThermiConfig { Overlays = [new OverlayConfig(), solo] } : new ThermiConfig());
        var before = _folder.Text();
        byte during = 0;

        Run(settings, host =>
        {
            var first = host.Overlays[0];
            var held = host.Overlays[drag == "merge" ? 1 : 0];
            PInvoke.GetWindowRect(first.Hwnd, out var rect);
            var front = new Position(rect.left + 3, (rect.top + rect.bottom) / 2);
            var pressed = drag == "merge" ? Middle(held.Hwnd) : PillOf(first, Metrics.GpuLoad.Id);
            MoveTo(host, held, pressed, PInvoke.WM_LBUTTONDOWN);
            if (drag == "back")
            {
                MoveTo(host, held, new Position(pressed.X + 20, pressed.Y));
                MoveTo(host, held, pressed);
            }

            _folder.Clock.Advance(DesktopGestures.Hold);
            PInvoke.PostMessage(held.Hwnd, PInvoke.WM_TIMER, OverlayGestures.HoldTimer, 0);
            HostRun.Pump(host);
            MoveTo(host, held, drag == "back" ? new Position(pressed.X + By.X, pressed.Y + By.Y) : front);
            during = held.Alpha;
            Post(held.Hwnd, PInvoke.WM_LBUTTONUP);
            HostRun.Until(host, _folder.Clock, () => _folder.Text() != before);
        });

        var file = _folder.Read();
        Assert.Equal(expected, $"{file.Overlays.Count} {file.Overlays[0].Items[0].Id} {(file.Overlays[0].Snapped is null ? "free" : "snapped")}");
        Assert.Equal(255, during);
    }

    // The cursor put at a point and the mouse's message sent, then the paints it asks for let out.
    private void MoveTo(OverlayHost host, OverlayWindow overlay, Position to, uint message = PInvoke.WM_MOUSEMOVE)
    {
        _cursor = to;
        Post(overlay.Hwnd, message);
        for (var paints = 0; paints < 3; paints++)
        {
            _folder.Clock.Advance(Settings.Interval);
            HostRun.Pump(host);
        }
    }

    private static Position Middle(HWND window) =>
        PInvoke.GetWindowRect(window, out var rect) ? new Position((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2) : new Position(0, 0);

    // The middle of the run of the overlay's middle row the item's pill is drawn on.
    private static Position PillOf(OverlayWindow overlay, string item)
    {
        PInvoke.GetWindowRect(overlay.Hwnd, out var rect);
        var y = (rect.top + rect.bottom) / 2;
        var on = Enumerable.Range(rect.left, rect.right - rect.left).Where(x => overlay.ItemAt(new Position(x, y)) == item).ToList();
        return new Position((on[0] + on[^1]) / 2, y);
    }

    // Esc comes as the overlay's hotkey, with the mouse still; or Esc and Alt are down as the move or the drop is sent, as
    // the window's thread reads them.
    private static void End(OverlayHost host, HWND overlay, string end)
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

    private static void Holding(byte key, Action send, OverlayHost host)
    {
        var keys = new byte[256];
        keys[key] = 0x80;
        TestPInvoke.SetKeyboardState(keys);
        send();
        HostRun.Pump(host);
        TestPInvoke.SetKeyboardState(new byte[256]);
    }

    // The windows the test's desktop shows, which only the host's process has windows on.
    private static List<HWND> Shown()
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

    private static Position Corner(HWND window) => PInvoke.GetWindowRect(window, out var rect) ? new Position(rect.left, rect.top) : new Position(0, 0);

    private static void Post(HWND window, uint message) => PInvoke.PostMessage(window, message, 0, 0);

    private void Run(Settings settings, Action<OverlayHost> body, Action<int>? openSettings = null) =>
        HostRun.Run(settings, _folder, _tray.Environment(_windowMutex, openSettings) with { Cursor = () => _cursor }, body);

    // A press in the overlay's middle, the cursor nudged by a pixel, within any drag distance, then moved by By, or by
    // `by`, the button still down; gives where the overlay is after each move.
    private (Position Nudged, Position Dragged) Drag(OverlayHost host, HWND overlay) => DragBy(host, overlay, By);

    private (Position Nudged, Position Dragged) DragBy(OverlayHost host, HWND overlay, Position by)
    {
        PInvoke.GetWindowRect(overlay, out var rect);
        var pressed = new Position((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
        _cursor = pressed;
        Post(overlay, PInvoke.WM_LBUTTONDOWN);
        HostRun.Pump(host);
        _cursor = new Position(pressed.X + 1, pressed.Y + 1);
        Post(overlay, PInvoke.WM_MOUSEMOVE);
        HostRun.Pump(host);
        var nudged = Corner(overlay);
        _cursor = new Position(pressed.X + by.X, pressed.Y + by.Y);
        Post(overlay, PInvoke.WM_MOUSEMOVE);
        HostRun.Pump(host);
        return (nudged, Corner(overlay));
    }
}
