using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Sensors;
using Thermi.Overlay;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Tests;

/// <summary>
/// The tests that run an <see cref="OverlayHost"/>, one at a time: its window classes are the process's, and the leak test
/// counts the whole process's handles.
/// </summary>
[CollectionDefinition(nameof(HostRun), DisableParallelization = true)]
public sealed class OneHostAtATime;

/// <summary>An overlay host driven from its own thread, a batch of messages at a time, on a recorded snapshot.</summary>
internal static class HostRun
{
    private const uint Pumped = PInvoke.WM_APP + 0x3FF;
    private const int MaxPumps = 1000;

    public static CultureInfo English { get; } = CultureInfo.GetCultureInfo("en-US");

    public static Snapshot Recorded { get; } = TestRecordings.Read(TestRecordings.Admin);

    /// <summary>The recording with every value 3 higher, so whatever shows a number shows another one.</summary>
    public static Snapshot Warmer { get; } = new([.. Recorded.Sensors.Select(sensor => sensor with { Value = sensor.Value + 3 })]);

    /// <summary>A window's mutex of the test's own, so a real settings window open meanwhile never counts.</summary>
    public static string WindowMutex() => @"Local\thermi-test-window-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Runs <paramref name="body"/> with a host of <paramref name="settings"/> on a desktop of the test's own, once it shows
    /// every overlay, on the recording or what <paramref name="latest"/> gives.
    /// </summary>
    public static void Run(Settings settings, SettingsFolder folder, HostEnvironment environment, Action<OverlayHost> body, Func<Snapshot?>? latest = null) =>
        TestWindows.OnOwnDesktop(() =>
        {
            using var host = new OverlayHost(settings, latest ?? (() => Recorded), English, folder.Clock, folder.Log, environment);
            host.Start();
            Until(host, folder.Clock, () => host.Overlays.Count == settings.Config.Overlays.Count);
            body(host);
        });

    /// <summary>Handles every message posted so far, the paints the throttle let out included.</summary>
    public static void Pump(OverlayHost host)
    {
        PInvoke.PostMessage(host.Hwnd, Pumped, 0, 0);
        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0 && message.message != Pumped)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    /// <summary>The once-a-second tick, as its timer would bring it, and what it lets out.</summary>
    public static void Tick(OverlayHost host)
    {
        PInvoke.PostMessage(host.Hwnd, PInvoke.WM_TIMER, OverlayHost.TickTimer, 0);
        Pump(host);
    }

    /// <summary>Pumps, a throttle's interval apart on the fake clock, until <paramref name="done"/>, as when a read ends on another thread.</summary>
    public static bool Until(OverlayHost host, FakeTimeProvider clock, Func<bool> done)
    {
        for (var pumps = 0; pumps < MaxPumps && !done(); pumps++)
        {
            clock.Advance(Settings.Interval);
            Pump(host);
        }

        return done();
    }
}
