using System.Globalization;
using Thermi.App.SettingsFile;
using Thermi.Core;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Windows.Overlay;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>The one process of the overlays: a layered window per overlay, and the host that a second thermi finds.</summary>
[Collection(nameof(HostRun))]
public sealed class OverlayHostTests : IDisposable
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly SettingsFolder _folder = new();
    private readonly FakeTray _tray = new();
    private readonly string _windowMutex = HostRun.WindowMutex();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task EachOverlayHasAWindowOfItsOwn()
    {
        var two = OverlayEdits.Snap(OverlayEdits.Add(new ThermiConfig()).Config, 1, new SnapConfig(SnapSpot.TopLeft)).Config;
        _folder.Write(ThermiConfigJson.Write(two));
        var settings = await _folder.LoadedAsync();
        var (shown, places, answered, after) = (0, 0, false, -1);

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, () => HostRun.Recorded, English, _folder.Clock, _folder.Log, _tray.Environment(_windowMutex));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 2);
            shown = host.Overlays.Count;
            places = host.Overlays.Select(overlay => PInvoke.GetWindowRect(overlay.Hwnd, out var rect) ? (rect.left, rect.top) : default).Distinct().Count();
            answered = OverlayHost.AskToShow();

            _folder.Write(ThermiConfigJson.Write(OverlayEdits.Delete(two, 0).Config));
            settings.FileChanged();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            after = host.Overlays.Count;
        });

        Assert.Equal((2, 2, true, 1), (shown, places, answered, after));
    }

    // Night QA: the overlay painted on its own second's tick, so the window, which shows each snapshot as it comes, could
    // show the next one. A snapshot shows on the overlay as it comes, before any tick.
    [Fact]
    public async Task ASnapshotShowsOnTheOverlayAsItComes()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        var now = HostRun.Recorded;
        var (before, after) = ("", "");

        HostRun.Run(settings, _folder, _tray.Environment(_windowMutex), host =>
        {
            string Shown()
            {
                _folder.Clock.Advance(Settings.Interval);
                HostRun.Pump(host);
                using var picture = host.Overlays[0].Picture();
                using var png = new MemoryStream();
                picture?.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                return Convert.ToBase64String(png.ToArray());
            }

            HostRun.Tick(host);
            before = Shown();
            now = HostRun.Warmer;
            host.Arrived();
            HostRun.Pump(host);
            after = Shown();
        }, () => now);

        Assert.NotEqual(before, after);
    }

    // What each press decides is Core's; one press proves the hotkey reaches every overlay.
    [Fact]
    public async Task TheHideHotkeyHidesEveryOverlay()
    {
        var settings = await _folder.LoadedAsync(OverlayEdits.Add(new ThermiConfig()).Config);
        var seen = new List<bool[]>();

        Run(settings, host =>
        {
            bool[] Shown()
            {
                return [.. host.Overlays.Select(overlay => TestWindows.IsWindowVisible(overlay.Hwnd))];
            }

            seen.Add(Shown());
            Press(host, HotkeyRegistry.IdOf(HotkeyRole.Toggle));
            seen.Add(Shown());
        });

        Assert.Equal([[true, true], [false, false]], seen);
    }

    [Fact]
    public async Task TheNextStyleHotkeyWritesTheNextSavedStyle()
    {
        var styles = new SavedStyles(Path.Combine(Path.GetDirectoryName(_folder.File)!, "styles"));
        styles.Save(new StyleConfig { Pills = PillTheme.Light }, "Daylight");
        styles.Save(new StyleConfig { Preset = StylePreset.Large }, "Big");
        var settings = await _folder.LoadedAsync(OverlayEdits.Add(new ThermiConfig()).Config);

        Run(settings, host =>
        {
            Press(host, HotkeyRegistry.IdOf(HotkeyRole.NextStyle));
            HostRun.Until(host, _folder.Clock, () => _folder.Written()?.Contains("\"Big\"", StringComparison.Ordinal) == true);
        });

        Assert.All(_folder.Read().Overlays, overlay => Assert.Equal(("Big", StylePreset.Large), (overlay.Style.Name, overlay.Style.Preset)));
    }

    // A click on the middle of the overlay reaches it, or with click-through on, what is below, and thermi's own icon
    // keeps its menu in reach meanwhile.
    [Fact]
    public async Task ClickThroughLetsEveryClickPassAndTurnsOffAgain()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Overlays = [new OverlayConfig { ClickThrough = true }] });
        var caught = new List<bool>();
        var icon = new List<bool>();

        Run(settings, host =>
        {
            var overlay = host.Overlays[0];
            bool Caught()
            {
                return PInvoke.GetWindowRect(overlay.Hwnd, out var rect)
                    && TestWindows.WindowAt(new Position((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2)) == overlay.Hwnd;
            }

            caught.Add(Caught());
            icon.Add(_tray.Shown.Count == 1);
            _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
            settings.FileChanged();
            HostRun.Until(host, _folder.Clock, Caught);
            caught.Add(Caught());
            icon.Add(_tray.Shown.Count == 1);
        });

        Assert.Equal([false, true], caught);
        Assert.Equal([true, true], icon);
    }

    // An overlay's own Hide decides whether it shows; thermi's icon shows either way, so the way back is always in reach.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AnOverlaysOwnHideDecidesItAndThermisIconAlwaysShows(bool hidden, bool overlay)
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Overlays = [new OverlayConfig { Hidden = hidden }] });
        var seen = (Overlay: false, Icon: false);

        Run(settings, host => seen = (TestWindows.IsWindowVisible(host.Overlays[0].Hwnd), _tray.Shown.ContainsKey(TrayScene.IdOf(null, FakeTray.ExePath))));

        Assert.Equal((overlay, true), seen);
    }

    // Every item but the power waits for an alert, which the recording's readings never reach.
    [Fact]
    public async Task WhileTheWindowIsOpenEveryItemShows()
    {
        var alerts = new ThermiConfig
        {
            Overlays = [new OverlayConfig { Items = [.. OverlayConfig.DefaultItems.Select(item => item with { AlertOnly = Threshold.Judges(item) })] }],
        };
        var settings = await _folder.LoadedAsync(alerts);
        var widths = new List<int>();

        Run(settings, host =>
        {
            int Width()
            {
                return PInvoke.GetWindowRect(host.Overlays[0].Hwnd, out var rect) ? rect.right - rect.left : 0;
            }

            widths.Add(Width());
            using (var window = MutexHolder.Holding(_windowMutex))
            {
                OverlayHost.AskToShow();
                HostRun.Until(host, _folder.Clock, () => Width() > widths[0]);
                widths.Add(Width());
            }

            HostRun.Tick(host);
            HostRun.Until(host, _folder.Clock, () => Width() == widths[0]);
            widths.Add(Width());
        });

        Assert.True(widths[1] > widths[0], string.Join(", ", widths));
        Assert.Equal(widths[0], widths[2]);
    }

    // The window's map marks each overlay where it shows: the place the window works out for it, while it is open, is
    // where the overlay's own window is, on the taskbar beside the notification area and in a corner.
    [Fact]
    public async Task TheWindowPlacesEachOverlayWhereItShows()
    {
        var two = OverlayEdits.Snap(OverlayEdits.Add(new ThermiConfig()).Config, 1, new SnapConfig(SnapSpot.TopLeft)).Config;
        var settings = await _folder.LoadedAsync(two);
        var (shown, placed) = (new List<Core.Bridge.Rect?>(), new List<Core.Bridge.Rect?>());

        Run(settings, host =>
        {
            List<Core.Bridge.Rect?> Shown()
            {
                return [.. host.Overlays.Select(overlay => PInvoke.GetWindowRect(overlay.Hwnd, out var rect)
                    ? new Core.Bridge.Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top)
                    : null)];
            }

            var input = new TrayInput(HostRun.Recorded, ServiceState.Running, English, Texts.English, LightTaskbar: false, ExePath: "") { WindowOpen = true };
            using var window = MutexHolder.Holding(_windowMutex);
            OverlayHost.AskToShow();
            placed.AddRange(Ui.Views.OverlayPlaces.Of(settings.Config, input, Desktop.Screens()));
            HostRun.Until(host, _folder.Clock, () => Shown().SequenceEqual(placed));
            shown.AddRange(Shown());
        });

        Assert.Equal(shown, placed);
    }

    // An overlay hidden on its own shows nowhere, so on the map it pushes no other along their taskbar, as on the desktop
    // its window, hidden, pushes none: the one beside it sits where it sits alone.
    [Fact]
    public void AHiddenOverlayPushesNoneOnTheMap()
    {
        var screen = new Screen("PC", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1)
        {
            Taskbar = new PixelRect(0, 1392, 2560, 1440),
        };
        var two = OverlayEdits.Add(new ThermiConfig()).Config;
        two = OverlayEdits.SetHidden(OverlayEdits.Snap(two, 1, new SnapConfig(SnapSpot.Taskbar)).Config, 1, hidden: true).Config;
        var input = new TrayInput(HostRun.Recorded, ServiceState.Running, English, Texts.English, LightTaskbar: false, ExePath: "") { WindowOpen = true };

        var alone = Ui.Views.OverlayPlaces.Of(two with { Overlays = [two.Overlays[0]] }, input, [screen]);
        var beside = Ui.Views.OverlayPlaces.Of(two, input, [screen]);

        Assert.Equal(alone[0], beside[0]);
    }

    // The menu opens the window, not the file, and turns the lock on and off for its own overlay.
    [Fact]
    public async Task TheMenuOpensTheWindowAndSetsTheLock()
    {
        var settings = await _folder.LoadedAsync(OverlayEdits.Add(new ThermiConfig { Language = "en" }).Config);
        var opened = new List<int>();
        var menus = new List<List<(nuint Id, string Text, bool Checked)>>();

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(
                settings,
                () => HostRun.Recorded,
                English,
                _folder.Clock,
                _folder.Log,
                _tray.Environment(_windowMutex, opened.Add));
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 2);
            menus.Add(HostMenus.Entries(host, 1));
            foreach (var text in new[] { MenuEntry.Settings, MenuEntry.Lock }.Select(Texts.English.Of))
            {
                HostMenus.Choose(host, 1, text);
            }

            HostRun.Until(host, _folder.Clock, () => _folder.Written()?.Contains("\"locked\"", StringComparison.Ordinal) == true);
            menus.Add(HostMenus.Entries(host, 1));
        });

        Assert.Equal([1], opened);
        Assert.Equal([(false, false), (false, true)], _folder.Read().Overlays.Select(overlay => (overlay.ClickThrough, overlay.Locked)));
        Assert.Equal([[], [Texts.English.Of(MenuEntry.Lock)]], menus.Select(menu => menu.Where(entry => entry.Checked).Select(entry => entry.Text).ToList()));
    }

    // An overlay that lets clicks through cannot take them back, and an item's icon may sit in the overflow: only
    // thermi's own icon, which shows while click-through is on, has it. Its notice comes once, after the icon.
    [Fact]
    public async Task ClickThroughIsOnlyInThermisMenuAndItsNoticeUndoesIt()
    {
        var settings = await _folder.LoadedAsync(WithTrayItems(new ThermiConfig { Language = "en" }));
        var (thermi, item) = (TrayScene.IdOf(null, FakeTray.ExePath), settings.Config.Overlays[0].Items[0].Id);
        var clickThrough = Texts.English.Of(MenuEntry.ClickThrough);
        var offered = new List<bool>();
        var (notices, iconFirst, undone) = (new List<string>(), false, false);

        Run(settings, host =>
        {
            foreach (var overlay in new int?[] { 0, TrayScene.OverlayOf(settings.Config, item), null })
            {
                offered.Add(HostMenus.Entries(host, overlay).Exists(entry => entry.Text == clickThrough));
            }

            HostMenus.Choose(host, null, clickThrough);
            HostRun.Until(host, _folder.Clock, () => Notices().Count > 0 && _folder.Written()?.Contains("\"clickThrough\"", StringComparison.Ordinal) == true);
            for (var second = 0; second < 3; second++)
            {
                _folder.Clock.Advance(TimeSpan.FromSeconds(1));
                HostRun.Tick(host);
            }

            notices.AddRange(Notices().Select(call => call.Notice));
            var added = _tray.Calls.FindIndex(call => call.Id == thermi && call.Message == NOTIFY_ICON_MESSAGE.NIM_ADD);
            iconFirst = added >= 0 && added < _tray.Calls.FindIndex(call => call.Notice.Length > 0);

            var callback = _tray.Calls.First(call => call.Id == thermi && call.Message == NOTIFY_ICON_MESSAGE.NIM_ADD).Callback;
            PInvoke.PostMessage(host.Hwnd, callback, 0, (nint)PInvoke.NIN_BALLOONUSERCLICK);
            undone = HostRun.Until(
                host,
                _folder.Clock,
                () => !settings.Config.Overlays[0].ClickThrough
                    && _folder.Written()?.Contains("\"clickThrough\"", StringComparison.Ordinal) == false);
        });

        var en = Texts.English;
        Assert.Equal([false, false, true], offered);
        Assert.Equal([en.Of(HostText.ClickThroughOnTitle) + "\n" + en.Of(HostText.ClickThroughOnText)], notices);
        Assert.True(iconFirst);
        Assert.True(undone);
        Assert.Single(Notices());
    }

    // A settings file under another file, where no folder can be made: the host says why in its own log, never the user's.
    [Fact]
    public async Task AFolderThatCannotBeWatchedIsLoggedInTheHostsLog()
    {
        _folder.Write("{}");
        using var store = new ConfigStore(Path.Combine(_folder.File, "config.json"), _folder.Clock);
        await using var settings = new Settings(store, _folder.Clock, _folder.Log);
        var log = new ListLogger();

        TestWindows.OnOwnDesktop(() =>
        {
            using (new OverlayHost(settings, () => HostRun.Recorded, English, _folder.Clock, log, _tray.Environment(_windowMutex)))
            {
                Assert.Single(log.Entries);
            }
        });
    }

    private static ThermiConfig WithTrayItems(ThermiConfig config) => config with
    {
        Overlays = [.. config.Overlays.Select(overlay => overlay with { Items = [.. overlay.Items.Select(item => item with { Tray = true })] })],
    };

    private List<FakeTray.Call> Notices() => [.. _tray.Calls.Where(call => call.Notice.Length > 0)];

    // The press, then the paint it asks for, once the paint throttle lets it out.
    private void Press(OverlayHost host, int hotkey)
    {
        PInvoke.PostMessage(host.Hwnd, PInvoke.WM_HOTKEY, (nuint)hotkey, 0);
        HostRun.Pump(host);
        _folder.Clock.Advance(Settings.Interval);
        HostRun.Pump(host);
    }

    private void Run(Settings settings, Action<OverlayHost> body) => HostRun.Run(settings, _folder, _tray.Environment(_windowMutex), body);
}
