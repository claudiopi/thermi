using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Ui;
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

    // A message, and the class of the hidden window it goes to, which acts on it and hands it on decoded as it was sent.
    [Theory]
    [InlineData("history-asked", Host)]
    [InlineData("history", Window)]
    [InlineData("double-click", Window)]
    [InlineData("click", Window)]
    [InlineData("edge", Window)]
    [InlineData("point", Window)]
    [InlineData("edited", Host)]
    [InlineData("gesture", Window)]
    [InlineData("raise", Window)]
    public async Task EachMessageReachesTheOtherProcess(string kind, string to)
    {
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
            using var beacon = new SettingsBeacon(_ => { }) { Received = received.Add };
            host.Start();

            delivered = HiddenWindow.Send(to, sent);
            HostRun.Pump(host);
        });

        Assert.True(delivered);
        var message = Assert.Single(received);
        Assert.Equal(WindowMessages.Encode(sent), WindowMessages.Encode(message));
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
