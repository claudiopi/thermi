using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Ui;
using Thermi.Ui.Web;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.Window;

/// <summary><c>thermi settings</c> before its window shows: a second request, and a failure on the window's thread, with a mutex and a settings file of the test's own.</summary>
public sealed class WindowProcessTests : IDisposable
{
    private readonly SettingsFolder _folder = new();

    // Of the test's own, so a real window open meanwhile never counts.
    private readonly string _mutex = @"Local\thermi-test-window-" + Guid.NewGuid().ToString("N");

    public void Dispose() => _folder.Dispose();

    // A window that has just taken its mutex has no beacon yet: a second request keeps looking for it for 2 s, then gives up.
    [Fact]
    public async Task ARequestWhileAWindowOpensWaitsForItsBeacon()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var raised = new List<int>();
        var waiting = new List<bool>();
        Task<int>? found = null;
        Task<int>? alone = null;

        using (MutexHolder.Holding(_mutex))
        {
            TestWindows.OnOwnDesktop(() =>
            {
                found = WindowProcess.RunAsync(2, Environment(), pseudo: false, _folder.Clock);
                _folder.Pass(TimeSpan.FromMilliseconds(500));
                waiting.Add(!found.IsCompleted);
                using (new SettingsBeacon(raised.Add))
                {
                    _folder.RunOut(found);
                }

                alone = WindowProcess.RunAsync(1, Environment(), pseudo: false, _folder.Clock);
                _folder.Pass(TimeSpan.FromMilliseconds(1900));
                waiting.Add(!alone.IsCompleted);
                _folder.RunOut(alone);
            });
        }

        Assert.Equal([true, true], waiting);
        Assert.Equal(0, await found!);
        Assert.Equal([2], raised);
        Assert.Equal(1, await alone!);
    }

    // What fails on the window's own thread reaches the process's entry, which logs it, instead of ending the process
    // with nothing in the log: here, a mutex whose name Windows cannot make.
    [Fact]
    public void AFailureOnTheWindowsThreadReachesItsCaller()
    {
        _folder.Write(ThermiConfigJson.Write(new ThermiConfig()));
        var environment = Environment() with { Mutex = @"Local\thermi-test\no-such-namespace" };

        var run = WindowProcess.RunAsync(1, environment, pseudo: false, _folder.Clock);
        _folder.RunOut(run);

        Assert.IsType<DirectoryNotFoundException>(run.Exception?.InnerException);
    }

    // Where WebView2's runtime is missing, the window says so in a message box, instead of vanishing with nothing but a
    // line in the log; answered No, it closes and the session ends without failing.
    [Fact]
    public async Task WithoutWebView2TheWindowSaysWhyAndCloses()
    {
        var settings = await _folder.LoadedAsync();
        var empty = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(_folder.File)!, "no-runtime")).FullName;
        var environment = Environment() with { BrowserFolder = empty, DataFolder = Path.GetDirectoryName(_folder.File)! };
        var told = false;

        TestWindows.OnOwnDesktop(() =>
        {
            TIMERPROC answer = (_, _, _, _) =>
            {
                var box = PInvoke.FindWindowEx(HWND.Null, HWND.Null, "#32770", null);
                if (!box.IsNull && PInvoke.IsWindowVisible(box))
                {
                    told = true;
                    PInvoke.PostMessage(box, TestPInvoke.WM_COMMAND, (nuint)MESSAGEBOX_RESULT.IDNO, 0);
                }
            };
            var timer = PInvoke.SetTimer(HWND.Null, 0, 50, answer);
            WindowSession.Run(new WindowStart(1, settings, Texts.English, environment, _folder.Clock));
            TestPInvoke.KillTimer(HWND.Null, timer);
            GC.KeepAlive(answer);
        });

        Assert.True(told);
    }

    private WindowEnvironment Environment() =>
        new(_mutex, _folder.File, Path.Combine(AppContext.BaseDirectory, "thermi-under-test.exe"), _folder.Log, _folder.Clock.GetUtcNow(), new TestFeed(), new StartWithWindows(@"Software\thermi-tests\Run", @"Software\thermi-tests\Approved", "thermi-under-test.exe"));
}
