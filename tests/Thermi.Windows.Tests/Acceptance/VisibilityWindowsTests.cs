using System.Globalization;
using Thermi.Core;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Overlay;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// Whether the overlay shows: the window, the hide hotkey and each overlay's own Hide.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class VisibilityWindowsTests : IDisposable
{
    // Combinations no app is likely to hold, so the tests never meet the user's own hotkeys.
    private static readonly HotkeysConfig First = new() { Toggle = "Ctrl+Alt+Shift+F21" };
    // The four hotkeys, read from the file, since click-through's and reset min/max's are fields of its new version.
    private static readonly HotkeysConfig Second = ThermiConfigJson.Read(
        ("{'version':2,'hotkeys':{'toggle':'Ctrl+Alt+Shift+F21','nextStyle':'Ctrl+Alt+Shift+F22',"
            + "'clickThrough':'Ctrl+Alt+Shift+F23','resetMinMax':'Ctrl+Alt+Shift+F24'}}").Replace('\'', '"')).Config.Hotkeys!;

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private readonly SettingsFolder _folder = new();
    private readonly string _windowMutex = HostRun.WindowMutex();

    public void Dispose() => _folder.Dispose();

    // A hidden overlay stays off the desktop while the window edits it, and after the window closes or crashes.
    [Fact]
    public async Task AHiddenOverlayStaysOffTheDesktopWhileEdited()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig { Overlays = [new OverlayConfig { Hidden = true }] }));
        var settings = await _folder.LoadedAsync();
        var seen = new List<bool>();

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = Host(settings);
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);
            bool shown()
            {
                return TestWindows.IsWindowVisible(host.Overlays[0].Hwnd);
            }

            seen.Add(shown());

            foreach (var gone in new Action<MutexHolder>[] { window => window.Release(), window => window.Crash() })
            {
                using var window = MutexHolder.Holding(_windowMutex);
                OverlayHost.AskToShow();
                seen.Add(HostRun.Until(host, _folder.Clock, shown));

                gone(window);
                HostRun.Tick(host);
                _folder.Clock.Advance(Settings.Interval);
                HostRun.Pump(host);
                seen.Add(shown());
            }
        });

        Assert.Equal([false, false, false, false, false], seen);
    }

    [Fact]
    public async Task TheHotkeyStatusIsTrustedOnlyForTheHotkeysShown()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig { Hotkeys = First }));
        var settings = await _folder.LoadedAsync();
        HotkeyFailures? beforeReading = HotkeyFailures.None, aboutTheFirst = null, afterReading = null;
        long? withoutOverlay = 0;

        TestWindows.OnOwnDesktop(() =>
        {
            using (var host = Host(settings))
            {
                host.Start();
                HostRun.Pump(host);

                // The window writes the second hotkeys and asks at once, before the overlay has read them.
                Assert.True(_folder.Settled(() => _folder.Store().WriteAsync(config => config with { Hotkeys = Second })).IsCompletedSuccessfully);
                var status = OverlayHost.AskHotkeyStatus()!.Value;
                (beforeReading, aboutTheFirst) = (Hotkeys.Trusted(status, Second), Hotkeys.Trusted(status, First));

                settings.FileChanged();
                HostRun.Until(host, _folder.Clock, () => Hotkeys.Trusted(OverlayHost.AskHotkeyStatus()!.Value, Second) is not null);
                afterReading = Hotkeys.Trusted(OverlayHost.AskHotkeyStatus()!.Value, Second);
            }

            withoutOverlay = OverlayHost.AskHotkeyStatus();
        });

        Assert.Null(beforeReading);
        Assert.Equal(HotkeyFailures.None, aboutTheFirst);
        Assert.Equal(HotkeyFailures.None, afterReading);
        Assert.Null(withoutOverlay);
    }

    [Fact]
    public async Task TheOverlayRegistersTheHotkeysOfItsOwnWrites()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var settings = await _folder.LoadedAsync();
        HotkeyFailures? trusted = null;

        TestWindows.OnOwnDesktop(() =>
        {
            using var host = Host(settings);
            host.Start();
            HostRun.Until(host, _folder.Clock, () => host.Overlays.Count == 1);

            // The window sets a hotkey, and the overlay is dragged before it has read it.
            Assert.True(_folder.Settled(() => _folder.Store().WriteAsync(config => config with { Hotkeys = First })).IsCompletedSuccessfully);
            settings.Release(new Press(new Position(100, 100), new Position(40, 90)), new Position(300, 260), new DragDistance(4, 4));
            HostRun.Until(host, _folder.Clock, () => Hotkeys.Trusted(OverlayHost.AskHotkeyStatus()!.Value, First) is not null);
            trusted = Hotkeys.Trusted(OverlayHost.AskHotkeyStatus()!.Value, First);
        });

        var written = _folder.Read();
        Assert.Equal((new Position(240, 250), First), (written.Overlays[0].Position, written.Hotkeys));
        Assert.Equal(HotkeyFailures.None, trusted);
    }

    // The other app holds the hide hotkey on a thread of its own, as a process of its own would.
    [Fact]
    public async Task TheHideHotkeyIsCheckedEvenWithEveryOverlayHidden()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig { Overlays = [new OverlayConfig { Hidden = true }], Hotkeys = Second }));
        var settings = await _folder.LoadedAsync();
        HotkeyFailures? refused = null;

        TestWindows.OnOwnDesktop(() =>
        {
            using var other = HotkeyHolder.Holding(Hotkeys.Parse(Second.Toggle)!.Value);
            Assert.True(other.Held);
            using var host = Host(settings);
            host.Start();
            HostRun.Pump(host);
            refused = Hotkeys.Trusted(OverlayHost.AskHotkeyStatus()!.Value, Second);
        });

        Assert.Equal(HotkeyFailures.Toggle, refused);
    }

    // While a hotkey's row records, the overlay lets its hotkeys go, so Windows gives the keys to the window; it takes them
    // again after, or once the window is gone. Its own combination is no other app's, whoever holds it now.
    [Fact]
    public async Task TheHotkeysRestWhileARowRecords()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig { Hotkeys = First }));
        var settings = await _folder.LoadedAsync();
        var held = new List<bool>();

        TestWindows.OnOwnDesktop(() =>
        {
            using var window = MutexHolder.Holding(_windowMutex);
            using var host = Host(settings);
            host.Start();
            HostRun.Pump(host);
            held.Add(HotkeyRegistry.HeldElsewhere(First.Toggle!, First));
            held.Add(HotkeyRegistry.HeldElsewhere(First.Toggle!, own: null));

            foreach (var after in new Action[] { () => HotkeyRegistry.AskToRest(false), window.Release })
            {
                HotkeyRegistry.AskToRest(true);
                held.Add(HotkeyRegistry.HeldElsewhere(First.Toggle!, own: null));
                after();
                HostRun.Tick(host);
                held.Add(HotkeyRegistry.HeldElsewhere(First.Toggle!, own: null));
            }
        });

        Assert.Equal([false, true, false, true, false, true], held);
    }

    private OverlayHost Host(Settings settings) =>
        new(settings, () => HostRun.Recorded, English, _folder.Clock, _folder.Log, new FakeTray().Environment(_windowMutex));
}
