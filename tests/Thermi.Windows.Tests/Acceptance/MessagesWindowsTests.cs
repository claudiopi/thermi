using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Ui;
using Thermi.Ui.Editor;
using Thermi.Ui.Web;
using Thermi.Windows.Tests.OnTop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Acceptance;

/// <summary>
/// The overlay and the window tell each other what is not a setting through their hidden windows, each kind as a
/// registered message or a <c>WM_COPYDATA</c>, within 500 ms.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class MessagesWindowsTests : IDisposable
{
    private const string Host = "thermi-host";
    private const string Window = "thermi-settings";

    private readonly SettingsFolder _folder = new();
    private readonly string _windowMutex = HostRun.WindowMutex();

    public void Dispose() => _folder.Dispose();

    // A message, and the class of the hidden window it goes to, which acts on it and hands it on decoded as it was sent;
    // a double-click on a value and a raise bring the window to the front, restored if it was minimized.
    [Theory]
    [InlineData("history-asked", Host, false)]
    [InlineData("history", Window, false)]
    [InlineData("double-click", Window, true)]
    [InlineData("click", Window, false)]
    [InlineData("edge", Window, false)]
    [InlineData("point", Window, false)]
    [InlineData("edited", Host, false)]
    [InlineData("gesture", Window, false)]
    [InlineData("raise", Window, true)]
    public async Task EachMessageReachesTheOtherProcess(string kind, string to, bool raises)
    {
        var raised = new List<int>();
        var config = new ThermiConfig { Overlays = [new OverlayConfig(), new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight) }] };
        _folder.Write(ThermiConfigJson.Write(config));
        var settings = await _folder.LoadedAsync();
        var sent = Message(kind, config);
        var received = new List<WindowMessage>();
        var delivered = false;

        TestWindows.OnOwnDesktop(() =>
        {
            using var window = MutexHolder.Holding(_windowMutex);
            using var host = new OverlayHost(
                settings,
                () => HostRun.Recorded,
                HostRun.English,
                _folder.Clock,
                _folder.Log,
                new FakeTray().Environment(_windowMutex))
            { Received = received.Add };
            using var beacon = new SettingsBeacon(raised.Add, _folder.Log) { Received = received.Add };
            host.Start();

            delivered = HiddenWindow.Send(to, sent);
            HostRun.Pump(host);
        });

        Assert.True(delivered);
        var message = Assert.Single(received);
        Assert.Equal(WindowMessages.Encode(sent), WindowMessages.Encode(message));
        Assert.Equal(raises, raised.Count == 1);
    }

    // QA round 5 (R5-07 and the owner): the window's marks guide the person while it shows: minimized, it tells the overlay
    // it edits none, so no value is marked nor overlay faded, and restored, the overlay and value it edits again.
    [Fact]
    public async Task AMinimizedWindowMarksNoOverlay()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig { Overlays = [new OverlayConfig(), new OverlayConfig()] }));
        var settings = await _folder.LoadedAsync();
        var sent = new List<Edited>();
        using var link = new OverlayLink(
            new Thermi.Core.Look.ChartHistory(_folder.Clock),
            new WindowEditor(settings, action => action(), _folder.Log, _folder.Clock),
            _ => { },
            () => { },
            _folder.Clock,
            sent.Add);

        link.Editing(1, Metrics.CpuLoad.Id, null);
        foreach (var shown in new[] { false, true })
        {
            _folder.Pass(TimeSpan.FromMilliseconds(200));
            link.Shown(shown);
        }

        Assert.Equal([new Edited(2, Metrics.CpuLoad.Id, null), new Edited(null, null, null), new Edited(2, Metrics.CpuLoad.Id, null)], sent);
    }

    // Windows calls the beacon from native code, where an exception would end the window's process: a message its
    // handler fails on is logged, and the next one still arrives.
    [Fact]
    public void AMessageTheWindowFailsOnLeavesItsBeaconAnswering()
    {
        var received = new List<WindowMessage>();
        var delivered = new List<bool>();

        TestWindows.OnOwnDesktop(() =>
        {
            using var beacon = new SettingsBeacon(_ => { }, _folder.Log)
            {
                Received = message =>
                {
                    received.Add(message);
                    if (received.Count == 1)
                    {
                        throw new InvalidOperationException("The window's handler failed.");
                    }
                },
            };

            delivered.Add(HiddenWindow.Send(Window, new Raise(1, null)));
            delivered.Add(HiddenWindow.Send(Window, new Raise(2, null)));
        });

        Assert.Equal((2, true), (received.Count, delivered[^1]));
        Assert.Contains(_folder.Log.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Error);
    }

    // Windows cannot say who sent a message, so the sender names its hidden window: another program's window of the
    // window's class, named as the sender, is no window of thermi's, and what it sends is dropped.
    [Fact]
    public async Task AWindowOfThermisClassInAnotherProgramIsNoPeer()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var settings = await _folder.LoadedAsync();
        var received = new List<WindowMessage>();
        var answered = false;

        TestWindows.OnOwnDesktop(desktop =>
        {
            using var window = MutexHolder.Holding(_windowMutex);
            using var host = new OverlayHost(
                settings,
                () => HostRun.Recorded,
                HostRun.English,
                _folder.Clock,
                _folder.Log,
                new FakeTray().Environment(_windowMutex))
            { Received = received.Add };
            host.Start();
            using var other = new OtherProcess(desktop, "thermi-other.exe");

            answered = SendClaiming(host.Hwnd, other.Raise(Window), new Edited(1, Metrics.CpuTemperature.Id, null));
            HostRun.Pump(host);
        });

        Assert.True(answered);
        Assert.Empty(received);
    }

    // A WM_COPYDATA of the message's kind that names <paramref name="sender"/> as the window it comes from.
    private static unsafe bool SendClaiming(HWND to, HWND sender, WindowMessage message)
    {
        var payload = WindowMessages.Encode(message);
        fixed (byte* data = payload)
        {
            var copy = new COPYDATASTRUCT
            {
                dwData = PInvoke.RegisterWindowMessage(WindowMessages.NameOf(message)),
                cbData = (uint)payload.Length,
                lpData = data,
            };
            return PInvoke.SendMessageTimeout(
                to,
                PInvoke.WM_COPYDATA,
                (nuint)(nint)sender,
                (nint)(&copy),
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG,
                500,
                out _) != 0;
        }
    }

    private static WindowMessage Message(string kind, ThermiConfig config) => kind switch
    {
        "history-asked" => new HistoryAsked(),
        "history" => new History([new ItemHistory(Metrics.CpuTemperature.Id, 1_000_000, TenMinutesWithAGap())]),
        "double-click" => new Select(1, Metrics.CpuTemperature.Id, SelectHow.DoubleClick),
        "click" => new Select(1, Metrics.GpuLoad.Id, SelectHow.Click),
        "edge" => new Select(2, null, SelectHow.Click),
        "point" => new Select(1, Metrics.RamUsed.Id, SelectHow.PointIn),
        "edited" => new Edited(1, Metrics.CpuTemperature.Id, Metrics.RamUsed.Id),
        "gesture" => new Gesture(
            GestureKind.Move,
            ThermiConfigJson.Write(config),
            ThermiConfigJson.Write(config with
            {
                Overlays = [config.Overlays[0] with { Position = new Position(400, 300), Snap = null }, config.Overlays[1]],
            })),
        _ => new Raise(2, Metrics.CpuFan.Id),
    };

    // Ten minutes, one slot a second, with the service stopped for the middle minute.
    private static float[] TenMinutesWithAGap() => [.. Enumerable.Range(0, 600).Select(second => second is >= 270 and < 330 ? float.NaN : 40 + (second % 20))];
}
