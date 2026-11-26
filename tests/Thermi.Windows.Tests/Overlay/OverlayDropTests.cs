using System.Drawing;
using Thermi.App.Gestures;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Windows.Overlay;
using Thermi.Windows.Win32;
using Windows.Win32;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>What a drag on an overlay shows on the overlays while it lasts and after its drop.</summary>
[Collection(nameof(HostRun))]
public sealed class OverlayDropTests : OverlayPresses, IDisposable
{
    public void Dispose() => Folder.Dispose();

    // With the window closed, the overlay a drag of another is over shows ringed in Windows' accent color.
    [Fact]
    public async Task TheOverlayADropWouldJoinIsRingedInTheAccentColor()
    {
        var solo = new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight), Items = [new ItemConfig { Id = "solo", Metric = Metrics.GpuTemperature.Id }] };
        var settings = await Folder.LoadedAsync(new ThermiConfig { Overlays = [new OverlayConfig(), solo] });
        Color? ring = null;

        Run(settings, host =>
        {
            var held = host.Overlays[1];
            MoveTo(host, held, Middle(held.Hwnd), PInvoke.WM_LBUTTONDOWN);
            MoveTo(host, held, DroppedAt(host, "merge", Nowhere));
            var shown = host.Overlays[0].OnScreenPicture!;
            ring = shown.GetPixel(shown.Width / 2, 1);
            End(host, held.Hwnd, "esc");
        });

        var accent = Desktop.Accent();
        Assert.Equal(Color.FromArgb(accent.R, accent.G, accent.B).ToArgb(), ring?.ToArgb());
    }

    // After a drop the overlay left under the pointer shows no tooltip until 1.5 s pass, whether the drop moved it, joined
    // another or made a new one: the GPU's pill lifted and dropped back where it was pressed, which is no click, or thrown
    // onto the desktop, and the overlay of one pill dropped on the other.
    [Theory]
    [InlineData("back")]
    [InlineData("off")]
    [InlineData("merge")]
    public async Task AfterADropTheOverlayUnderThePointerShowsNoTooltipForAWhile(string drag)
    {
        var solo = new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight), Items = [new ItemConfig { Id = "solo", Metric = Metrics.GpuTemperature.Id }] };
        var settings = await Folder.LoadedAsync(drag == "merge" ? new ThermiConfig { Overlays = [new OverlayConfig(), solo] } : new ThermiConfig());
        var (quiet, after) = ((bool?)null, (bool?)null);

        Run(settings, host =>
        {
            var held = host.Overlays[drag == "merge" ? 1 : 0];
            var pressed = drag == "merge" ? Middle(held.Hwnd) : PillOf(held, Metrics.GpuLoad.Id);
            MoveTo(host, held, pressed, PInvoke.WM_LBUTTONDOWN);
            Hold(host, held);
            var dropped = drag == "merge" ? DroppedAt(host, drag, pressed) : new Position(pressed.X + By.X, pressed.Y + By.Y);
            MoveTo(host, held, dropped);
            dropped = drag == "back" ? pressed : dropped;
            MoveTo(host, held, dropped);
            End(host, held.Hwnd, "drop");
            HostRun.Until(host, Folder.Clock, () => host.Overlays.Count == (drag == "off" ? 2 : 1) && Under(host, dropped) is not null);
            var under = Under(host, dropped)!;
            quiet = !under.TipsShow;
            Folder.Clock.Advance(DropQuiet.Lasts);
            PInvoke.PostMessage(under.Hwnd, PInvoke.WM_TIMER, ValueTips.QuietTimer, 0);
            HostRun.Pump(host);
            after = under.TipsShow;
        });

        Assert.Equal((true, true), (quiet, after));
    }

    // A value held in the CPU pill lifts its item alone, whose empty slot takes its value's place and leaves the pill its label.
    [Fact]
    public async Task AnItemLiftedAloneLeavesItsPillItsLabel()
    {
        var settings = await Folder.LoadedAsync(new ThermiConfig());
        var (before, lifted, value) = (new List<int>(), new List<int>(), new List<int>());

        Run(settings, host =>
        {
            var overlay = host.Overlays[0];
            var label = EndOf(overlay, Metrics.CpuTemperature.Id, label: true);
            PInvoke.GetWindowRect(overlay.Hwnd, out var rect);
            before.AddRange(Column(overlay.OnScreenPicture!, label.X - rect.left));
            var held = EndOf(overlay, Metrics.CpuTemperature.Id, label: false);
            value.AddRange(Column(overlay.OnScreenPicture!, held.X - rect.left - 12));
            MoveTo(host, overlay, held, PInvoke.WM_LBUTTONDOWN);
            Hold(host, overlay);
            HostRun.Tick(host);
            lifted.AddRange(Column(overlay.OnScreenPicture!, label.X - rect.left));
            value = [.. value.Zip(Column(overlay.OnScreenPicture!, held.X - rect.left - 12), (was, now) => was - now).Where(change => change != 0)];
            End(host, overlay.Hwnd, "esc");
        });

        Assert.Equal(before, lifted);
        Assert.NotEmpty(value);
    }

    // The 12 columns of pixels from x on, as ARGB.
    private static List<int> Column(Bitmap picture, int x) =>
        [.. Enumerable.Range(x, 12).SelectMany(at => Enumerable.Range(0, picture.Height).Select(y => picture.GetPixel(at, y).ToArgb()))];

    private static OverlayWindow? Under(OverlayHost host, Position at) =>
        host.Overlays.FirstOrDefault(overlay => overlay.OnScreen && PInvoke.GetWindowRect(overlay.Hwnd, out var rect)
            && at.X >= rect.left && at.X < rect.right && at.Y >= rect.top && at.Y < rect.bottom);

}
