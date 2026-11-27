using Thermi.Core.Settings;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// The overlay shown on a test's desktop as a person sees it: two overlays alike, one on the panel or the top bar and one
/// on a window of another program, fed by the real service from a recorded tree, with the screen before and after.
/// </summary>
internal sealed record Scene(TestDesktop Desktop, TestWindow Window, string Before, string Screen, string Picture)
{
    /// <summary>Where the first overlay sits: on the top bar or a top panel, at the screen's top edge.</summary>
    public static readonly Position OnPanel = new(200, 0);

    /// <summary>Where the second sits: on the window, below every panel.</summary>
    public static readonly Position OnWindow = new(200, 500);

    /// <summary>
    /// Shows thermi on <paramref name="desktop"/> over a window, full screen or not, with the sensors serving or stopped, and
    /// takes the screen before and after, and the picture <c>thermi preview</c> paints for the same snapshot and scale.
    /// </summary>
    public static async Task<Scene> ShowAsync(
        TestDesktop desktop, double scale, bool fullScreen, bool stopped, CancellationToken cancel, bool clickThrough = false)
    {
        var overlay = OverlayConfig.First;
        await desktop.WriteSettingsAsync(
            ThermiConfig.New with { Overlays = [overlay with { Position = OnPanel }, overlay with { Position = OnWindow, ClickThrough = clickThrough }] },
            cancel);
        if (!stopped)
        {
            await SensorFeed.ServeAsync(desktop.Session, SensorFeed.Desktop, cancel);
        }

        var window = await desktop.OpenWindowAsync(fullScreen, cancel);
        var before = await desktop.ScreenshotAsync(cancel);
        await desktop.ShowThermiAsync([], cancel);
        var screen = await desktop.ScreenshotAsync(cancel);
        var picture = await desktop.PreviewAsync(stopped ? null : SensorFeed.Desktop, scale, cancel);
        return new Scene(desktop, window, before, screen, picture);
    }

    /// <summary>The pixels of the picture the screen shows otherwise at <paramref name="at"/>: 0 when the overlay shows there as painted.</summary>
    public Task<long> DifferingAtAsync(Position at, CancellationToken cancel) =>
        Pictures.DifferingAsync(Desktop.Session, Screen, Picture, at.X, at.Y, cancel);

    /// <summary>The pixels of the whole screen that thermi changed: 0 when it shows nothing.</summary>
    public async Task<long> ChangedAsync(CancellationToken cancel)
    {
        var (width, height) = await Pictures.SizeAsync(Desktop.Session, Screen, cancel);
        return await Pictures.ChangedAsync(Desktop.Session, Before, Screen, (0, 0, width, height), cancel);
    }

    /// <summary>Clicks where the overlay on the window draws nothing, and returns what the window says it got.</summary>
    public async Task<string?> ClickThroughAsync(CancellationToken cancel)
    {
        var (x, y) = await Pictures.NothingAtAsync(Desktop.Session, Picture, cancel);
        await Desktop.Input.ClickAsync(OnWindow.X + x, OnWindow.Y + y, cancel);
        return await Window.NextAsync(cancel);
    }
}
