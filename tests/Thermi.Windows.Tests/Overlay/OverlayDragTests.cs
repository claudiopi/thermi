using Thermi.Core.Settings;
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

    // Another window took the mouse mid-drag, as a menu or a dialog does: the overlay goes back to its snap, and the
    // button let go after moves nothing.
    [Fact]
    public async Task LosingTheMouseMidDragPutsTheOverlayBack()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig());
        var before = _folder.Text();
        var (start, during, back, after) = (Nowhere, Nowhere, false, Nowhere);

        Run(settings, host =>
        {
            var overlay = host.Overlays[0].Hwnd;
            start = Corner(overlay);
            during = Drag(host, overlay).Dragged;
            using (var other = new HiddenTestWindow("thermi-test-" + Guid.NewGuid().ToString("N")))
            {
                PInvoke.SetCapture(other.Hwnd);
                back = HostRun.Until(host, _folder.Clock, () => Corner(overlay) == start);
                PInvoke.ReleaseCapture();
            }

            Post(overlay, PInvoke.WM_LBUTTONUP);
            HostRun.Pump(host);
            HostRun.Tick(host);
            after = Corner(overlay);
        });

        Assert.NotEqual(start, during);
        Assert.True(back, "The overlay stayed where the drag had it.");
        Assert.Equal(start, after);
        Assert.Null(settings.Config.Overlays[0].Position);
        Assert.Equal(before, _folder.Text());
    }

    private static Position Corner(HWND window) => PInvoke.GetWindowRect(window, out var rect) ? new Position(rect.left, rect.top) : new Position(0, 0);

    private static void Post(HWND window, uint message) => PInvoke.PostMessage(window, message, 0, 0);

    private void Run(Settings settings, Action<OverlayHost> body, Action<int>? openSettings = null) =>
        HostRun.Run(settings, _folder, _tray.Environment(_windowMutex, openSettings) with { Cursor = () => _cursor }, body);

    // A press in the overlay's middle, the cursor nudged by a pixel, within any drag distance, then moved by By, the
    // button still down; gives where the overlay is after each move.
    private (Position Nudged, Position Dragged) Drag(OverlayHost host, HWND overlay)
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
        _cursor = new Position(pressed.X + By.X, pressed.Y + By.Y);
        Post(overlay, PInvoke.WM_MOUSEMOVE);
        HostRun.Pump(host);
        return (nudged, Corner(overlay));
    }
}
