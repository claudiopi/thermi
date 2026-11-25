using Thermi.Core;
using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.Tests;

/// <summary>What the overlay's menu changes, and which overlay the window opens on; <c>CommandLineTests</c> reads the command line.</summary>
public sealed class WindowRequestTests
{
    [Fact]
    public void ClickThroughAndLockTurnOnAndOffForOneOverlay()
    {
        var two = OverlayEdits.Add(new ThermiConfig()).Config;

        var through = OverlayEdits.SetClickThrough(two, 1, on: true).Config;
        var locked = OverlayEdits.SetLocked(through, 1, on: true).Config;
        var free = OverlayEdits.SetLocked(OverlayEdits.SetClickThrough(locked, 1, on: false).Config, 1, on: false).Config;

        Assert.Equal([(false, false), (true, true)], locked.Overlays.Select(overlay => (overlay.ClickThrough, overlay.Locked)));
        Assert.Equal(ThermiConfigJson.Write(two), ThermiConfigJson.Write(free));
        Assert.Equal(Refusal.NotFound, OverlayEdits.SetClickThrough(two, 2, on: true).Refused);
        Assert.Equal(Refusal.NotFound, OverlayEdits.SetLocked(two, -1, on: true).Refused);
    }

    [Theory]
    [InlineData(2, 0, 3, 1)]
    [InlineData(4, 1, 3, 0)]
    [InlineData(0, 2, 3, 2)]
    [InlineData(0, 2, 2, 0)]
    public void TheWindowOpensOnTheOverlayAskedForOrTheFirst(int requested, int current, int count, int shown)
    {
        Assert.Equal(shown, WindowRequest.OverlayOf(requested, current, count));
    }
}
