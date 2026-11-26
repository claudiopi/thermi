using Thermi.App.SettingsFile;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Gestures;
using Thermi.Windows.Overlay;
using Thermi.Windows.Win32;
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

    // A locked overlay takes no drag, nor a press held still on a value until it would lift, and a double-click on it still
    // opens the window, which its window class must let Windows send.
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
            MoveTo(host, host.Overlays[0], EndOf(host.Overlays[0], Metrics.CpuLoad.Id, label: false), PInvoke.WM_LBUTTONDOWN);
            Hold(host, host.Overlays[0]);
            MoveTo(host, host.Overlays[0], new Position(_cursor.X + By.X, _cursor.Y + By.Y));
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
    // another shows whole, not faded, and joins it before the pill it is let go on, but not with Alt held; a lifted pill
    // dropped on the overlay of one pill joins it before that pill, and thrown far onto the desktop becomes an overlay of
    // its own; and the overlay of many dragged over the one of one pill joins it with the cursor beside it. In the CPU pill,
    // a value held lifts its item alone and the label the pill, and the CPU temperature's value dropped on the end of the
    // CPU power's moves it to the end of its pill. The overlays left, as their count, each one's first item and whether the
    // first is still snapped.
    [Theory]
    [InlineData("own", "1 gpu.load snapped")]
    [InlineData("back", "1 cpu.temperature free")]
    [InlineData("merge", "1 solo snapped")]
    [InlineData("alt", "2 cpu.temperature,solo snapped")]
    [InlineData("lifted", "2 cpu.temperature,gpu.load snapped")]
    [InlineData("over", "1 cpu.temperature snapped")]
    [InlineData("off", "2 cpu.temperature,gpu.load snapped")]
    [InlineData("value", "2 cpu.temperature,cpu.load snapped")]
    [InlineData("label", "2 gpu.load,cpu.temperature snapped")]
    [InlineData("within", "1 fan.cpu snapped")]
    public async Task APressLiftsOnlyHeldStillAndADropJoinsWhereItIsLetGo(string drag, string expected)
    {
        var solo = new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight), Items = [new ItemConfig { Id = "solo", Metric = Metrics.GpuTemperature.Id }] };
        var two = drag is "merge" or "alt" or "lifted" or "over";
        var settings = await _folder.LoadedAsync(two ? new ThermiConfig { Overlays = [new OverlayConfig(), solo] } : new ThermiConfig());
        var before = _folder.Text();
        byte during = 0;

        Run(settings, host =>
        {
            var held = host.Overlays[drag is "merge" or "alt" ? 1 : 0];
            var pressed = PressedAt(host, drag);
            MoveTo(host, held, pressed, PInvoke.WM_LBUTTONDOWN);
            if (drag == "back")
            {
                MoveTo(host, held, new Position(pressed.X + 20, pressed.Y));
                MoveTo(host, held, pressed);
            }

            if (drag != "over")
            {
                Hold(host, held);
            }

            MoveTo(host, held, DroppedAt(host, drag, pressed));
            during = held.Alpha;
            End(host, held.Hwnd, drag == "alt" ? "alt" : "drop");
            HostRun.Until(host, _folder.Clock, () => _folder.Text() != before);
        });

        var file = _folder.Read();
        var firsts = string.Join(',', file.Overlays.Select(overlay => overlay.Items[0].Id));
        Assert.Equal(expected, $"{file.Overlays.Count} {firsts} {(file.Overlays[0].Snapped is null ? "free" : "snapped")}");
        Assert.Equal(255, during);
    }

    // An overlay in its middle, a value or the label in the CPU pill, or the GPU's pill.
    private static Position PressedAt(OverlayHost host, string drag) => drag switch
    {
        "merge" or "alt" => Middle(host.Overlays[1].Hwnd),
        "over" => Middle(host.Overlays[0].Hwnd),
        "value" => EndOf(host.Overlays[0], Metrics.CpuLoad.Id, label: false),
        "label" => EndOf(host.Overlays[0], Metrics.CpuTemperature.Id, label: true),
        "within" => EndOf(host.Overlays[0], Metrics.CpuTemperature.Id, label: false),
        _ => PillOf(host.Overlays[0], Metrics.GpuLoad.Id),
    };

    // In front of the first overlay's first pill, or of the overlay of one pill, or, for the overlay of many held in its
    // middle, 20 px left of the overlay of one pill, the overlay of many then over all of it; or on the CPU power's value.
    private static Position DroppedAt(OverlayHost host, string drag, Position pressed)
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

    // The press held still until it lifts, as the hold's timer wakes the overlay.
    private void Hold(OverlayHost host, OverlayWindow held)
    {
        _folder.Clock.Advance(DesktopGestures.Hold);
        PInvoke.PostMessage(held.Hwnd, PInvoke.WM_TIMER, OverlayGestures.HoldTimer, 0);
        HostRun.Pump(host);
    }

    // On the overlay's middle row, 2 px into an item: from its start, on the label of the pill it starts, or from its end,
    // on its value.
    private static Position EndOf(OverlayWindow overlay, string item, bool label)
    {
        PInvoke.GetWindowRect(overlay.Hwnd, out var rect);
        var y = (rect.top + rect.bottom) / 2;
        var on = Enumerable.Range(rect.left, rect.right - rect.left).Where(x => overlay.ItemAt(new Position(x, y)) == item).ToList();
        return new Position(label ? on[0] + 2 : on[^1] - 2, y);
    }

    private static Position Middle(HWND window) =>
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
