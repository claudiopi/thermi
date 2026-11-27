using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Overlay;
using Thermi.Windows.Tests.Processes;
using Thermi.Windows.Ui;
using Thermi.Windows.Ui.Web;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Window;

/// <summary>
/// The page as the build embeds it, in a real WebView2, as <c>thermi settings</c> shows it beside an overlay's host: each
/// of A200's messages to the window reaches the page, which tells the overlay what it edits next, and nothing is logged
/// as a warning or an error, the page's own reports included. WebView2's processes take their parent's desktop, not its
/// thread's, so the walk runs in the test assembly started again on a desktop of the test's own, and says how it went
/// in a file. <c>scripts/smoke-window.ps1</c> clicks through an installed build the same way.
/// </summary>
[Collection(nameof(HostRun))]
public sealed class RealPageTests
{
    private const string WalkVariable = "THERMI_TEST_REAL_PAGE";
    private static readonly TimeSpan MostWait = TimeSpan.FromMinutes(2);

    [Fact]
    public void TheBuiltPageTakesEachMessageInARealWebView2()
    {
        var told = Path.Combine(AppContext.BaseDirectory, $"real-page-{Guid.NewGuid():N}.txt");
        uint ended = 1;

        TestWindows.OnOwnDesktop(desktop => ended = RunOn(desktop, told));

        var said = File.Exists(told) ? File.ReadAllText(told) : "The walk wrote nothing.";
        File.Delete(told);
        Assert.True(ended == 0, said);
    }

    // The test assembly again, on the desktop, told where to write how its walk went. Unelevated, as thermi runs: WebView2
    // in an elevated process starts its browser through explorer.exe, on the user's desktop, which no window of another
    // desktop can host, so the controller fails with "Invalid window handle".
    private static uint RunOn(string desktop, string told)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "Thermi.Windows.Tests.exe");
        var variables = new Dictionary<string, string>(StringComparer.Ordinal) { [WalkVariable] = told };
        using var walk = DesktopProcess.Start(exe, [], variables, desktop, unelevated: true);
        walk.Input.Close();

        // The real clock only bounds a walk that hangs, which is then ended.
        return walk.WaitForExit(MostWait) ? (uint)walk.ExitCode : 2;
    }

#pragma warning disable CA2255 // The one way to run before xunit's entry point, in the test assembly's own process only.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void WalkIfAsked()
    {
        if (Environment.GetEnvironmentVariable(WalkVariable) is not { } told)
        {
            return;
        }

        var failed = Walk(told);
#pragma warning disable RS0030 // Ends the helper before xunit's entry point would run the tests in it.
        Environment.Exit(failed ? 1 : 0);
#pragma warning restore RS0030
    }

    // Apart, so that compiling the initializer loads no thermi type, as OtherProcess's. On the initializer's own thread,
    // made single-threaded as the window's own: another thread would wait for the initializer to end before it could run
    // any code of this assembly.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Walk(string told)
    {
        string said;
        try
        {
            // The runtime made it multi-threaded as it started; the helper ends after the walk, so it may change.
            TestPInvoke.CoUninitialize();
            TestPInvoke.CoInitializeEx(COINIT.COINIT_APARTMENTTHREADED).ThrowOnFailure();
            said = PageWalk.Run();
        }
#pragma warning disable CA1031 // Said in the file, which the test reads, instead of ending the helper with nothing to read.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            said = ex.ToString();
        }

        File.WriteAllText(told, said);
        return said.Length > 0;
    }

    /// <summary>
    /// The walk through A200's messages to the window, woken by a Win32 timer that only wakes the loop: once the page has
    /// told the overlay anything, so it runs, each step's message goes to the window, and the next once the page told what
    /// that one should. The test's clock moves on at each tick, for the window's throttle of what it tells; 600 ticks from
    /// before the page starts, 30 s or more as Windows rounds a timer up, end the walk, the page's start included.
    /// </summary>
    private sealed class PageWalk(List<(WindowMessage Sent, Edited? Then)> steps, FakeTimeProvider clock)
    {
        private const int MostTicks = 600;

        private readonly List<Edited> _edits = [];

        // How the walk went, for a failure to tell a page slow to start from one that stopped answering or a window that
        // closed on its own: the tick the page first told the overlay anything, each step's sends and answer, and whether
        // the ticks ran out.
        private readonly int?[] _sentAt = new int?[steps.Count];
        private readonly int?[] _answeredAt = new int?[steps.Count];
        private readonly int[] _refused = new int[steps.Count];
        private int? _firstTold;
        private int? _gaveUp;

        private int _ticks;
        private bool _sent;

        // Whether the page had the keyboard's focus as the window first showed, so Ctrl+Z works without a click first.
        private bool? _focused;

        private int Reached { get; set; }

        private int Count => steps.Count;

        private bool Done => Reached == Count;

        private IReadOnlyList<Edited> Edits
        {
            get
            {
                lock (_edits)
                {
                    return [.. _edits];
                }
            }
        }

        /// <summary>
        /// Walks a window of settings of its own; empty when every step was answered and nothing was logged as a warning, else
        /// what went wrong.
        /// </summary>
        public static string Run()
        {
            using var folder = new SettingsFolder();
            var config = new ThermiConfig { Overlays = [new OverlayConfig(), new OverlayConfig { Snap = new SnapConfig(SnapSpot.TopRight) }] };
            folder.Write(ThermiConfigJson.Write(config));
            var settings = folder.Settings();
            if (!folder.Settled(settings.SyncAsync).IsCompletedSuccessfully)
            {
                return "The settings did not load.";
            }

            var walk = new PageWalk(Steps(config), folder.Clock);
            var mutex = HostRun.WindowMutex();
            using (MutexHolder.Holding(mutex))
            using (var host = new OverlayHost(
                settings,
                () => HostRun.Recorded,
                HostRun.English,
                folder.Clock,
                folder.Log,
                new FakeTray().Environment(mutex))
            { Received = walk.Told })
            {
                host.Start();
                TIMERPROC tick = (_, _, _, _) => walk.Tick();
                var timer = PInvoke.SetTimer(HWND.Null, 0, 50, tick);
                WindowSession.Run(new WindowStart(0, settings, Texts.English, Environment(folder, mutex), folder.Clock));
                TestPInvoke.KillTimer(HWND.Null, timer);
                GC.KeepAlive(tick);
            }

            var warned = folder.Log.Entries.Where(entry => entry.Level >= LogLevel.Warning).Select(entry => entry.Message).ToList();
            return walk.Done && walk._focused == true && warned.Count == 0 ? "" : walk.Said(warned, folder.Log.Entries);
        }

        public void Told(WindowMessage message)
        {
            if (message is Edited edited)
            {
                lock (_edits)
                {
                    _edits.Add(edited);
                }
            }
        }

        private string Said(List<string> warned, IReadOnlyList<ListLogger.Entry> log)
        {
            var ended = _gaveUp is { } at ? $"the walk gave up at tick {at}" : $"the window closed on its own after {_ticks} ticks";
            var each = Enumerable.Range(0, Count).Select(step => $"{step}: sent {_sentAt[step]}, refused {_refused[step]}, answered {_answeredAt[step]}");
            return $"Answered {Reached} of {Count} steps; {(Done ? "every step answered" : ended)}; the page first told the overlay at tick "
                + $"{_firstTold}; by step, at ticks {string.Join(" | ", each)}; the page had the focus: {_focused}; "
                + $"told the overlay {string.Join(", ", Edits)}; logged {string.Join(" | ", warned)}; "
                + $"its log {string.Join(" | ", log.Select(entry => $"{entry.Level} {entry.Message}"))}";
        }

        // A200's messages to the window, each with what the page then tells the overlay it edits, or null for one that
        // changes what the page shows but not what it edits, as a point only marks a row; the next step then shows the
        // page still answers.
        private static List<(WindowMessage Sent, Edited? Then)> Steps(ThermiConfig config) =>
        [
            (new Select(1, Metrics.RamUsed.Id, SelectHow.DoubleClick), new Edited(1, Metrics.RamUsed.Id, null)),
            (
                new History(
                [
                    new ItemHistory(Metrics.CpuTemperature.Id, 1_000_000, [.. Enumerable.Range(0, 600).Select(second => 40f + (second % 20))]),
                ]),
                null),
            (new Select(1, Metrics.GpuLoad.Id, SelectHow.Click), new Edited(1, Metrics.GpuLoad.Id, null)),
            (new Select(1, Metrics.RamUsed.Id, SelectHow.PointIn), null),
            (new Select(1, Metrics.RamUsed.Id, SelectHow.PointOut), null),
            (
                new Gesture(
                    GestureKind.Move,
                    ThermiConfigJson.Write(config),
                    ThermiConfigJson.Write(config with
                    {
                        Overlays = [config.Overlays[0] with { Position = new Position(400, 300), Snap = null }, config.Overlays[1]],
                    })),
                null),
            (new Select(2, null, SelectHow.Click), new Edited(2, null, null)),
            (new Raise(1, Metrics.CpuFan.Id), new Edited(1, Metrics.CpuFan.Id, null)),
        ];

        private static WindowEnvironment Environment(SettingsFolder folder, string mutex)
        {
            var startup = new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi-under-test.exe");
            var exe = Path.Combine(AppContext.BaseDirectory, "thermi-under-test.exe");
            return new(mutex, folder.File, exe, folder.Log, folder.Clock.GetUtcNow(), new TestFeed { Latest = HostRun.Recorded }, startup)
            {
                // Of the tests' own, and kept: WebView2's processes hold its profile a while after the window closes.
                DataFolder = Path.Combine(AppContext.BaseDirectory, "real-page-profile"),
                HotkeyStatus = () => null,
                RestHotkeys = _ => false,
                AltGrKeys = () => new HashSet<int>(),
            };
        }

        private void Tick()
        {
            var tick = ++_ticks;
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var edits = Edits;
            _firstTold ??= edits.Count > 0 ? tick : null;
            if (_sent && (steps[Reached].Then is not { } then || (edits.Count > 0 && edits[^1] == then)))
            {
                _answeredAt[Reached] = tick;
                (Reached, _sent) = (Reached + 1, false);
            }

            if (Done || tick > MostTicks)
            {
                _gaveUp ??= Done ? null : tick;
                PInvoke.PostMessage(PInvoke.FindWindowEx(HWND.Null, HWND.Null, "thermi-window", null), PInvoke.WM_CLOSE, 0, 0);
            }
            else if (!_sent && edits.Count > 0)
            {
                _focused ??= FocusInPage();
                _sent = HiddenWindow.Send(HiddenWindow.WindowClass, steps[Reached].Sent);
                _sentAt[Reached] ??= _sent ? tick : null;
                _refused[Reached] += _sent ? 0 : 1;
            }
        }

        // The window's thread has the focus on one of the page's windows inside it, not on the window itself.
        private static unsafe bool FocusInPage()
        {
            var window = PInvoke.FindWindowEx(HWND.Null, HWND.Null, "thermi-window", null);
            var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
            return TestPInvoke.GetGUIThreadInfo(TestPInvoke.GetWindowThreadProcessId(window, null), ref info)
                && TestPInvoke.IsChild(window, info.hwndFocus);
        }
    }
}
