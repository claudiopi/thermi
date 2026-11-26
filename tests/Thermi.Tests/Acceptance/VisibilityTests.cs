using System.Text.Json;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Whether the overlay shows: the window, the hide hotkey's override and the visibility setting.
/// </summary>
public sealed class VisibilityTests
{
    private static readonly Screen Left = new(@"\\.\DISPLAY1", Primary: true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1);

    private static readonly Screen Right = new(@"\\.\DISPLAY2", Primary: false, new PixelRect(2560, 0, 4480, 1080), new PixelRect(2560, 0, 4480, 1032), 1);

    // Two overlays on each monitor: the default snap, one snapped on the right, one dropped on the right, and one dropped
    // off every monitor, which shows at the default snap.
    private static readonly ThermiConfig FourOverlays = new()
    {
        Overlays =
        [
            new OverlayConfig(),
            new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopLeft, Right.Id) },
            new OverlayConfig { Position = new Position(3000, 500) },
            new OverlayConfig { Position = new Position(-900, 500) },
        ],
    };

    private static readonly ForegroundWindow InFront = App(new PixelRect(3000, 200, 3400, 600)) with { ClassName = "WorkerW", Monitor = Right.Bounds };

    // The window in front, as the overlay reads it on its tick.
    private static readonly Dictionary<string, ForegroundWindow?> Apps = new(StringComparer.Ordinal)
    {
        ["none"] = null,
        ["right"] = App(Right.Bounds),
        ["left with borders"] = App(new PixelRect(-8, -8, 2568, 1448)),
        ["desktop"] = App(Left.Bounds) with { ClassName = "WorkerW" },
        ["start menu"] = App(Left.Bounds) with { ClassName = "XamlExplorerHostIslandWindow" },
        ["maximized"] = App(new PixelRect(-8, -8, 2568, 1448)) with { Maximized = true },
        ["minimized"] = App(Left.Bounds) with { Minimized = true },
        ["work area"] = App(Left.Work),
        ["in front, right"] = InFront,
        ["in front, unknown monitor"] = InFront with { Monitor = null },
    };

    private static ThermiConfig Saved(string? name) => new() { Overlays = [new OverlayConfig { Style = new StyleConfig { Name = name } }] };

    // Which overlays a full-screen app covers, 1 each: one that covers its monitor hides only that monitor's overlays; the
    // desktop, the shell, maximized and minimized windows are none. Windows' answer of exclusive full screen or presentation
    // mode names no monitor, so it counts on the monitor of the window in front, or on all of them when that is unknown.
    [Theory] // Rows: each a shell class of a static set, an operand of the cover's condition, or a bound of the monitor's rect.
    [InlineData("right", NotificationState.Normal, "0110")]
    [InlineData("left with borders", NotificationState.Normal, "1001")]
    [InlineData("desktop", NotificationState.Normal, "0000")]
    [InlineData("start menu", NotificationState.Normal, "0000")]
    [InlineData("maximized", NotificationState.Normal, "0000")]
    [InlineData("minimized", NotificationState.Normal, "0000")]
    [InlineData("work area", NotificationState.Normal, "0000")]
    [InlineData("in front, right", NotificationState.Presentation, "0110")]
    [InlineData("in front, unknown monitor", NotificationState.Direct3DFullScreen, "1111")]
    [InlineData("none", NotificationState.Direct3DFullScreen, "1111")]
    [InlineData("in front, right", NotificationState.Direct3DFullScreen, "0000", false)]
    public void AFullScreenAppCoversTheOverlaysOfItsMonitor(string app, NotificationState state, string covered, bool monitors = true)
    {
        var flags = OverlayVisibility.FullScreen(FourOverlays, monitors ? [Left, Right] : [], Apps[app], state);

        Assert.Equal(covered, string.Concat(flags.Select(flag => flag ? '1' : '0')));
    }

    // The current style's name, the saved styles' names, and the names each press gives; none saved leaves all as it is.
    [Theory] // Rows: each a start: a current style, none, none saved.
    [InlineData("B", "A,C,B", "C,A,B")]
    [InlineData(null, "B,A", "A")]
    [InlineData(null, "", "")]
    public void TheNextStyleGoesRoundTheSavedOnesFromTheFirst(string? current, string saved, string pressed)
    {
        StyleConfig[] styles = [.. saved.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(name => new StyleConfig { Name = name })];
        var config = Saved(current);

        var names = new List<string?>();
        for (var press = 0; press < pressed.Split(',', StringSplitOptions.RemoveEmptyEntries).Length; press++)
        {
            config = OverlayEdits.NextStyle(config, styles);
            names.Add(config.Overlays[0].Style.Name);
        }

        Assert.Equal(pressed, string.Join(',', names));
        Assert.True(
            styles.Length > 0 || ReferenceEquals(config, OverlayEdits.NextStyle(config, styles)),
            "With no saved style, the next style changes nothing.");
    }

    // Overlays as their visibility, with +hidden for one hidden on its own; steps of D (decide), H ("Hide overlays" from a
    // menu), K (the hide hotkey), C (a click on thermi's icon), R (the file read again) or N (thermi started again), each
    // then a for an app in full screen, then = and how each overlay shows: 1 or 0. The flag lives in memory, so the file
    // never holds it.
    [Theory] // Rows: each a way the flag is set or cleared, a second click among them.
    [InlineData("Always,Always+hidden", "D=10 H=00 R=00 C=10 N=10")]
    [InlineData("HideInFullscreen", "Da=0 K=0 Da=0 D=0 K=1")]
    [InlineData("Always", "K=0 D=0 C=1 C=1")]
    [InlineData("Always,OnlyInFullscreen", "D=10 H=00 K=10 Da=11")]
    public void HideOverlaysIsAFlagThatIsNeverSaved(string overlays, string steps)
    {
        var parts = overlays.Split(',').Select(overlay => overlay.Split('+')).Select(part =>
            $"{{'visibility':'{JsonNamingPolicy.KebabCaseLower.ConvertName(part[0])}','hidden':{(part.Length > 1 ? "true" : "false")}}}");
        var file = $"{{'version':2,'overlays':[{string.Join(',', parts)}]}}".Replace('\'', '"');
        var config = ThermiConfigJson.Read(file).Config;
        var visibility = new OverlayVisibility();

        foreach (var step in steps.Split(' '))
        {
            var (what, expected) = (step[..step.IndexOf('=', StringComparison.Ordinal)], step[(step.IndexOf('=', StringComparison.Ordinal) + 1)..]);
            (config, visibility) = what[0] switch
            {
                'R' => (ThermiConfigJson.Read(ThermiConfigJson.Write(config)).Config, visibility),
                'N' => (config, new OverlayVisibility()),
                _ => (config, visibility),
            };
            Act(visibility, what[0]);

            var shown = visibility.Decide(config, [.. config.Overlays.Select(_ => what.EndsWith('a'))]);

            Assert.Equal((step, expected), (step, string.Concat(shown.Select(each => each == OverlayShown.Shown ? '1' : '0'))));
            Assert.Equal(ThermiConfigJson.Write(ThermiConfigJson.Read(file).Config), ThermiConfigJson.Write(config));
        }
    }

    private static ForegroundWindow App(PixelRect bounds) => new("UnrealWindow", bounds, Maximized: false, Minimized: false);

    private static void Act(OverlayVisibility visibility, char what)
    {
        switch (what)
        {
            case 'H':
                visibility.HideOverlays = true;
                break;
            case 'K':
                visibility.PressHotkey();
                break;
            case 'C':
                Assert.Equal(visibility.HideOverlays, visibility.ClickIcon());
                break;
            default:
                break;
        }
    }
}
