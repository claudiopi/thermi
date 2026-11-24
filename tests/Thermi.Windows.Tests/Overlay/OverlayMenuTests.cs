using Thermi.Commands;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;
using Thermi.Overlay;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Overlay;

/// <summary>
/// The overlay's menu and thermi's icon's, chosen as the menu gives back the entry picked, on a desktop of the test's own:
/// each entry changes the file or the overlays.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class OverlayMenuTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly SettingsFolder _folder = new();
    private readonly FakeTray _tray = new();
    private readonly string _windowMutex = HostRun.WindowMutex();

    public void Dispose() => _folder.Dispose();

    // The highest temperature since the start, until the reset starts it again from the value now.
    [Fact]
    public async Task ResetMinAndMaxStartsTheOverlaysMaximumAgain()
    {
        var hottest = OverlayConfig.DefaultItems[1] with { Stat = ItemStat.Max };
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en", Overlays = [new OverlayConfig { Items = [hottest] }] });
        var now = HostRun.Warmer;
        var shown = new List<double?>();

        HostRun.Run(settings, _folder, _tray.Environment(_windowMutex), host =>
        {
            var item = settings.Config.Overlays[0].Items[0];
            void Arrive(Snapshot snapshot)
            {
                now = snapshot;
                host.Arrived();
                HostRun.Pump(host);
                _folder.Clock.Advance(TimeSpan.FromSeconds(1));
            }

            double? Max()
            {
                return host.History.Shown(item, SensorMap.Read(now, settings.Config.Overlays[0])[item.Id])?.Value;
            }

            Arrive(HostRun.Warmer);
            Arrive(HostRun.Recorded);
            shown.Add(Max());
            HostMenus.Choose(host, 0, "Reset min and max");
            Arrive(HostRun.Recorded);
            shown.Add(Max());
        }, () => now);

        var (warmer, recorded) = (Temperature(HostRun.Warmer), Temperature(HostRun.Recorded));
        Assert.Equal(recorded + 3, warmer);
        Assert.Equal([warmer, recorded], shown);
    }

    // Exit takes the host's window down, which ends the overlay process's message loop, and so does a tool or a script
    // that closes the host's window or the overlay Windows calls the process's main window.
    [Theory]
    [InlineData("menu")]
    [InlineData("host")]
    [InlineData("overlay")]
    public async Task ExitEndsTheOverlayProcess(string how)
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        var (before, after, quit) = (false, true, false);

        Run(settings, host =>
        {
            before = TestPInvoke.IsWindow(host.Hwnd);
            switch (how)
            {
                case "menu":
                    HostMenus.Choose(host, null, "Exit thermi");
                    break;
                default:
                    PInvoke.SendMessage(how == "host" ? host.Hwnd : host.Overlays[0].Hwnd, PInvoke.WM_CLOSE, 0, 0);
                    break;
            }

            after = TestPInvoke.IsWindow(host.Hwnd);
            quit = TestPInvoke.PeekMessage(out _, default, TestPInvoke.WM_QUIT, TestPInvoke.WM_QUIT, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE);
        });

        Assert.Equal((true, false, true), (before, after, quit));
    }

    // Starting a process behind UAC holds whoever starts it until the prompt closes: "Start sensors" returns to the host's
    // loop at once, and the host answers while the prompt is still open.
    [Fact]
    public async Task StartSensorsNeverHoldsTheHostWhileItsPromptIsOpen()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        using var asked = new ManualResetEventSlim();
        using var prompt = new ManualResetEventSlim();
        var closed = false;
        var environment = _tray.Environment(_windowMutex) with
        {
            StartSensors = () =>
            {
                asked.Set();
                prompt.Wait(Patience, TestContext.Current.CancellationToken);
                Volatile.Write(ref closed, true);
                return Task.FromResult<int?>(StartSensors.Started);
            },
        };
        var answeredWhileOpen = false;

        HostRun.Run(settings, _folder, environment, host =>
        {
            host.Tray!.Choose(new MenuRow(MenuEntry.StartSensors), null);
            answeredWhileOpen = asked.Wait(Patience, TestContext.Current.CancellationToken) && OverlayHost.AskToShow() && !Volatile.Read(ref closed);
            prompt.Set();
        });

        Assert.True(answeredWhileOpen, "The host waited for the prompt to close.");
    }

    // The picture is painted as the overlay shows, on the clipboard the test gives the host in place of Windows' own.
    [Fact]
    public async Task CopyAsImagePutsTheOverlayOnTheClipboard()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        System.Drawing.Size? copied = null;
        var visible = 0;
        var shown = default(System.Drawing.Size);
        var environment = _tray.Environment(_windowMutex) with
        {
            CopyImage = (_, picture) =>
            {
                copied = picture.Size;
                for (var x = 0; x < picture.Width; x += 4)
                {
                    visible += picture.GetPixel(x, picture.Height / 2).A > 0 ? 1 : 0;
                }
            },
        };

        HostRun.Run(settings, _folder, environment, host =>
        {
            HostMenus.Choose(host, 0, "Copy as image");
            PInvoke.GetWindowRect(host.Overlays[0].Hwnd, out var rect);
            shown = new System.Drawing.Size(rect.right - rect.left, rect.bottom - rect.top);
        });

        Assert.Equal(shown, copied);
        Assert.True(visible > 0, "The picture is blank.");
    }

    // The once-a-second raise that keeps an overlay above the taskbar never lifts it over its own menu while that is open.
    [Fact]
    public async Task TheOverlaysMenuStaysAboveTheOverlay()
    {
        var settings = await _folder.LoadedAsync(new ThermiConfig { Language = "en" });
        bool? above = null;

        Run(settings, host =>
        {
            using var watcher = new MenuWatcher(() =>
            {
                if (MenuWatcher.OpenMenu() is not { IsNull: false } menu)
                {
                    return false;
                }

                PInvoke.SendMessage(host.Hwnd, PInvoke.WM_TIMER, OverlayHost.TickTimer, 0);
                above = MenuWatcher.Above(menu, host.Overlays[0].Hwnd);
                return true;
            });
            host.Tray!.Menu(0);
        });

        Assert.True(above, "The overlay rose over its own menu.");
    }

    private static double? Temperature(Snapshot snapshot) =>
        SensorMap.Read(snapshot, new OverlayConfig())[OverlayConfig.DefaultItems[1].Id].Value;

    private void Run(Settings settings, Action<OverlayHost> body) => HostRun.Run(settings, _folder, _tray.Environment(_windowMutex), body);
}
