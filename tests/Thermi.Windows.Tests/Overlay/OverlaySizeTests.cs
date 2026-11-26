using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Win32;
using Windows.Win32;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>An overlay's size against its monitor's: never larger than the work area it shows in.</summary>
[Collection(nameof(HostRun))]
public sealed class OverlaySizeTests : IDisposable
{
    private readonly SettingsFolder _folder = new();
    private readonly FakeTray _tray = new();
    private readonly string _windowMutex = HostRun.WindowMutex();

    public void Dispose() => _folder.Dispose();

    // At 400 % with 36 px text, every item far wider than any monitor, an overlay free at its monitor's corner or snapped to
    // its top right shows smaller, whole within the work area, where the window's map places it too, and its Size row says so;
    // and so does one at 100 % with items enough to pass any monitor's width.
    [Theory]
    [InlineData("free")]
    [InlineData("corner")]
    [InlineData("free at 100 %")]
    public async Task AnOverlayLargerThanItsMonitorShowsSmallerWithinIt(string where)
    {
        var screen = Desktop.Screens().First(each => each.Primary);
        var text = new TextStyle(36, TextWeight.Bold);
        var big = where == "free at 100 %"
            ? new OverlayConfig
            {
                Items = [.. Enumerable.Repeat(OverlayConfig.DefaultItems, 10).SelectMany(items => items).Select(item => item with { Id = "" })],
            }
            : new OverlayConfig(OverlayConfig.MaxSize);
        big = big with { Style = new StyleConfig { Preset = StylePreset.Custom, Label = text, Value = text } };
        big = where == "corner"
            ? big with { Snap = new SnapConfig(SnapSpot.TopRight) }
            : big with { Position = new Position(screen.Work.Left, screen.Work.Top) };
        var settings = await _folder.LoadedAsync(ItemIds.Assign(new ThermiConfig { Overlays = [big] }));
        var (shown, placed, fitted) = ((PixelRect?)null, (PixelRect?)null, (IReadOnlyList<bool>)[]);

        HostRun.Run(settings, _folder, _tray.Environment(_windowMutex), host =>
        {
            PInvoke.GetWindowRect(host.Overlays[0].Hwnd, out var rect);
            shown = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
            var input = new TrayInput(HostRun.Recorded, ServiceState.Running, HostRun.English, Texts.English, LightTaskbar: false, ExePath: "");
            var map = Ui.Views.OverlayPlaces.Of(settings.Config, input, Desktop.Screens())[0]!;
            placed = new PixelRect(map.X, map.Y, map.X + map.Width, map.Y + map.Height);
            using var painter = new Painting.Painter();
            fitted = Ui.Views.OverlayPlaces.Fitted(settings.Config, input, Desktop.Screens, _ => painter);
        });

        Assert.True(shown is { } box && screen.Work.Contains(box), $"{shown} leaves {screen.Work}.");
        Assert.Equal(shown, placed);
        Assert.Equal([true], fitted);
    }
}
