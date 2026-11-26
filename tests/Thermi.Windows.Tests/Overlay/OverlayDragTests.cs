using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>What a drag on an overlay moves, lifts and writes.</summary>
[Collection(nameof(HostRun))]
public sealed class OverlayDragTests : OverlayPresses, IDisposable
{
    public void Dispose() => Folder.Dispose();

    // The overlay of several pills, pressed on one of them, follows the cursor whole only past the drag distance, stays
    // where it was let go, and the file gets that spot, off its snap.
    [Fact]
    public async Task ADragMovesTheOverlayAndWritesWhereItWasDropped()
    {
        var settings = await Folder.LoadedAsync(new ThermiConfig());
        var (start, nudged, during, after, written) = (Nowhere, Nowhere, Nowhere, Nowhere, false);

        Run(settings, host =>
        {
            var overlay = host.Overlays[0].Hwnd;
            start = Corner(overlay);
            (nudged, during) = Drag(host, overlay);
            Post(overlay, PInvoke.WM_LBUTTONUP);
            written = HostRun.Until(host, Folder.Clock, () => Folder.Written()?.Contains("\"position\"", StringComparison.Ordinal) == true);
            HostRun.Tick(host);
            after = Corner(overlay);
        });

        var dropped = new Position(start.X + By.X, start.Y + By.Y);
        Assert.True(written, "The drop was never written.");
        Assert.Equal((start, dropped, dropped), (nudged, during, after));
        Assert.Equal((dropped, null), (Folder.Read().Overlays[0].Position, Folder.Read().Overlays[0].Snap));
    }

    // A locked overlay takes no drag, nor a press held still on a value until it would lift, and a double-click on it still
    // opens the window, which its window class must let Windows send.
    [Fact]
    public async Task ALockedOverlayStaysWhereItIs()
    {
        var settings = await Folder.LoadedAsync(new ThermiConfig { Overlays = [new OverlayConfig { Locked = true }] });
        var before = Folder.Text();
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
            MoveTo(host, host.Overlays[0], new Position(Pointer.X + By.X, Pointer.Y + By.Y));
            Post(overlay, PInvoke.WM_LBUTTONUP);
            HostRun.Pump(host);
            HostRun.Tick(host);
            corners.Add(Corner(overlay));
            Post(overlay, PInvoke.WM_LBUTTONDBLCLK);
            HostRun.Pump(host);
        }, opened.Add);

        Assert.Single(corners.Distinct());
        Assert.Null(settings.Config.Overlays[0].Position);
        Assert.Equal(before, Folder.Text());
        Assert.Equal([0], opened);
        Assert.True(OverlayWindow.WindowClass(TestWindows.DefaultProcedure, default).style.HasFlag(WNDCLASS_STYLES.CS_DBLCLKS));
    }

    // A double-click on a value asks an open window to select it, and opens the window, or brings it up, whenever none took
    // that: none runs, or one of its class answers without taking it, as a window closing or still starting does.
    [Theory]
    [InlineData("none", true)]
    [InlineData("drops", true)]
    [InlineData("takes", false)]
    public async Task ADoubleClickOnAValueOpensTheWindowUnlessOneTookIt(string window, bool opens)
    {
        var settings = await Folder.LoadedAsync(new ThermiConfig());
        var (opened, taken) = (new List<int>(), new List<WindowMessage>());

        Run(settings, host =>
        {
            using var beacon = window == "takes" ? new SettingsBeacon(_ => { }, Folder.Log) { Received = taken.Add } : null;
            if (window == "drops")
            {
                HiddenWindow.Register(HiddenWindow.WindowClass, name => HiddenWindow.Plain(TestWindows.DefaultProcedure, name));
            }

            var drops = window == "drops" ? HiddenWindow.Create(HiddenWindow.WindowClass) : HWND.Null;
            MoveTo(host, host.Overlays[0], EndOf(host.Overlays[0], Metrics.CpuLoad.Id, label: false));
            Post(host.Overlays[0].Hwnd, PInvoke.WM_LBUTTONDBLCLK);
            HostRun.Pump(host);
            if (!drops.IsNull)
            {
                PInvoke.DestroyWindow(drops);
                HiddenWindow.Unregister(HiddenWindow.WindowClass);
            }
        }, opened.Add);

        Assert.Equal(opens ? [0] : [], opened);
        Assert.Equal(!opens, taken.Any(message => message is Select { How: SelectHow.DoubleClick }));
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
        var settings = await Folder.LoadedAsync(new ThermiConfig());
        var before = Folder.Text();
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
            written = HostRun.Until(host, Folder.Clock, () => Folder.Text() != before);
            HostRun.Tick(host);
            after = Corner(overlay);
            left.AddRange(Shown());
        });

        Assert.True(during.Count > 1, "No snap mark showed during the drag.");
        Assert.Equal([overlay], left);
        var file = Folder.Read().Overlays[0];
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
        var settings = await Folder.LoadedAsync(two ? new ThermiConfig { Overlays = [new OverlayConfig(), solo] } : new ThermiConfig());
        var before = Folder.Text();
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
            HostRun.Until(host, Folder.Clock, () => Folder.Text() != before);
        });

        var file = Folder.Read();
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

}
