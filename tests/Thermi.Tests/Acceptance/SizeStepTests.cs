using System.Globalization;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// The sizes the overlay's menu offers and the wheel turns to, which Core works out from the size the overlay shows, so
/// the overlay only writes them.
/// </summary>
public sealed class SizeStepTests
{
    // A 2560 × 1440 monitor at 100 % with a 48 px taskbar, where an overlay shows at most 47.99 / 38 = 126 %.
    private static readonly Screen Pc = new("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1)
    {
        Taskbar = new PixelRect(0, 1392, 2560, 1440),
    };

    // An overlay, free or on the taskbar, at a size; the presets offered, "Bigger" and "Smaller" ("-" for none), then the
    // wheel's turns one after another, in Windows' 120ths of a notch, and the size each leaves ("-" for no change).
    [Theory] // Rows: a step either way, each bound, between steps, a touchpad's part turns and a turn back, past the taskbar's cap and from above it
    [InlineData("free 1", "0.75 1 1.25 1.5 2", "1.05 0.95", "120 120 -120", "1.05 1.1 1.05")]
    [InlineData("free 0.75", "0.75 1 1.25 1.5 2", "0.8 -", "-120", "-")]
    [InlineData("free 1.23", "0.75 1 1.25 1.5 2", "1.25 1.2", "40 40 40", "- - 1.25")]
    [InlineData("free 4", "0.75 1 1.25 1.5 2", "- 3.95", "60 -120 240", "- 3.95 4")]
    [InlineData("taskbar 1.25", "0.75 1 1.25", "1.3 1.2", "120 120", "1.3 -")]
    [InlineData("taskbar 2", "0.75 1 1.25", "- 1.25", "120 -120", "- 1.25")]
    public void TheSizeStepsFromWhatTheOverlayShowsWithinItsBounds(string overlay, string presets, string steps, string turns, string sizes)
    {
        var words = overlay.Split(' ');
        var shown = new OverlayConfig(Number(words[1])) { Position = words[0] == "free" ? new Position(100, 100) : null };
        var turned = new List<string>();
        var (now, carried) = (shown, 0);
        foreach (var delta in turns.Split(' ').Select(int.Parse))
        {
            (var size, carried) = OverlaySizes.Wheel(now, [Pc], carried, delta);
            turned.Add(size is { } to ? Text(to) : "-");
            now = size is { } next ? now with { Size = next } : now;
        }

        Assert.Equal(presets, string.Join(' ', OverlaySizes.Offered(shown, [Pc]).Select(Text)));
        Assert.Equal(steps, $"{Maybe(OverlaySizes.Bigger(shown, [Pc]))} {Maybe(OverlaySizes.Smaller(shown, [Pc]))}");
        Assert.Equal(sizes, string.Join(' ', turned));
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(double size) => size.ToString(CultureInfo.InvariantCulture);

    private static string Maybe(double? size) => size is { } some ? Text(some) : "-";
}
