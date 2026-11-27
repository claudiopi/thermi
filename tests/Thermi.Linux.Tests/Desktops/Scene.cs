using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// The overlay shown on a test's desktop as a person sees it: two overlays alike, one on the panel or the top bar and one
/// on a window of another program, fed by the real service one steady second, with the screen before and after.
/// </summary>
internal sealed record Scene(TestDesktop Desktop, SessionProcess Thermi, TestWindow Window, string Before, string Screen, string Picture)
{
    /// <summary>
    /// Where the first overlay sits: on the top bar or a top panel, 200 pixels from the screen's left edge, snapped to it, as
    /// only a snap puts an overlay on a panel; the panel centers it, so its top is found within its own height.
    /// </summary>
    public static readonly Position OnPanel = new(200, 0);

    /// <summary>Where the second sits: on the window, below every panel.</summary>
    public static readonly Position OnWindow = new(200, 500);

    /// <summary>
    /// The first overlay with only what one second shows alike every second, so what it paints never changes as it runs:
    /// no graph, which grows with each second, and none of the values read as a change since the second before; and small
    /// enough to show at its size on GNOME's top bar, which shrinks a larger one to fit.
    /// </summary>
    public static readonly OverlayConfig Overlay = OverlayConfig.First with
    {
        Size = 0.75,
        Graph = GraphStyle.None,
        Items = [.. OverlayConfig.First.Items.Where(item => item.Metric != Metrics.CpuLoad.Id && item.Metric != Metrics.CpuPower.Id)],
    };

    /// <summary>
    /// Shows thermi on <paramref name="desktop"/> over a window, full screen or not, with the sensors serving or stopped, and
    /// takes the screen before and after, and the picture <c>thermi preview</c> paints for the same snapshot and scale.
    /// </summary>
    public static async Task<Scene> ShowAsync(
        TestDesktop desktop, double scale, bool fullScreen, bool stopped, CancellationToken cancel, bool clickThrough = false)
    {
        var onPanel = Overlay with { Position = null, Snap = new SnapConfig(SnapSpot.Taskbar) { Offset = OnPanel.X } };
        await desktop.WriteSettingsAsync(
            ThermiConfig.New with { Overlays = [onPanel, Overlay with { Position = OnWindow, ClickThrough = clickThrough }] },
            cancel);
        var snapshot = stopped ? null : (await SensorFeed.ServeSteadyAsync(desktop.Session, SensorFeed.Desktop, cancel)).Snapshot;
        var window = await desktop.OpenWindowAsync(fullScreen, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        var thermi = await desktop.ShowThermiAsync([], cancel);
        var screen = await desktop.ScreenshotAsync(cancel);
        var picture = await desktop.PreviewAsync(snapshot, scale, cancel);
        return new Scene(desktop, thermi, window, before, screen, picture);
    }

    /// <summary>
    /// The pixels of the picture the screen shows otherwise at <paramref name="at"/>: 0 when the overlay shows there as
    /// painted. On the panel, the fewest at any height the picture's own from its top.
    /// </summary>
    public async Task<long> DifferingAtAsync(Position at, CancellationToken cancel)
    {
        var (_, height) = at == OnPanel ? await Pictures.SizeAsync(Desktop.Session, Picture, cancel) : (0, 0);
        var fewest = long.MaxValue;
        for (var y = at.Y; y <= at.Y + height && fewest > 0; y++)
        {
            fewest = Math.Min(fewest, await Pictures.DifferingAsync(Desktop.Session, Before, Screen, Picture, at.X, y, cancel));
        }

        return fewest;
    }

    /// <summary>The pixels of the whole screen that thermi changed: 0 when it shows nothing.</summary>
    public async Task<long> ChangedAsync(CancellationToken cancel)
    {
        var (width, height) = await Pictures.SizeAsync(Desktop.Session, Screen, cancel);
        return await Pictures.ChangedAsync(Desktop.Session, Before, Screen, (0, 0, width, height), cancel);
    }

    /// <summary>Clicks where the overlay on the window draws nothing, and returns what the window says it got first.</summary>
    public async Task<string?> ClickThroughAsync(CancellationToken cancel)
    {
        var (x, y) = await Pictures.NothingAtAsync(Desktop.Session, Picture, cancel);
        return await ClickAsync(OnWindow.X + x, OnWindow.Y + y, cancel);
    }

    /// <summary>
    /// Clicks the left button at a point, then the middle one where no overlay is, and returns what the window says it got
    /// first: <c>click 1</c> when the first click reached it, <c>click 2</c> when the overlay took it, so the test never
    /// waits for a click that does not come.
    /// </summary>
    public async Task<string?> ClickAsync(int x, int y, CancellationToken cancel)
    {
        await Desktop.Input.ClickAsync(x, y, cancel);
        await Desktop.Input.ClickAsync(OnWindow.X / 2, OnWindow.Y + 300, cancel, button: 2);
        return await Window.NextAsync(cancel);
    }
}
