using System.Drawing;
using Thermi.Core.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Windows.Tests.OnTop;

/// <summary>
/// The overlay above the taskbar, on a desktop of the test's own, where another process raises its windows over it: a
/// taskbar's makes it rise again, any other's it leaves alone, or it would fight every other always-on-top app.
/// </summary>
public sealed class TopmostTests
{
    private const nuint Waiting = 0x7A;

    // How long a window's change of order may take to reach the overlay's thread, in ticks of 20 ms.
    private const int Patience = 500;

    // The overlay's value tooltip rises with it, so the overlay never covers a tooltip that shows.
    [Fact]
    public void ATaskbarRaisedAboveTheOverlayRaisesItAgain()
    {
        var seen = new List<(bool Covered, bool Above, bool TipAbove)>();

        OnTop((overlay, _, other) =>
        {
            using var tips = new Thermi.Overlay.ValueTips(overlay);
            var tip = Owned(overlay);
            for (var round = 0; round < 2; round++)
            {
                var taskbar = other.Raise("taskbar");
                var covered = Above(taskbar, overlay);
                Pump(overlay, () => Above(overlay, taskbar));
                seen.Add((covered, Above(overlay, taskbar), Above(tip, overlay)));
            }
        });

        Assert.Equal([(true, true, true), (true, true, true)], seen);
    }

    // Its change of order reaches a hook of the test's own as it reaches the overlay's, which has had it by then; the
    // once-a-second safety net, which raises it too, leaves it below as well.
    [Fact]
    public void AnyOtherWindowRaisedAboveTheOverlayStaysAbove()
    {
        var (covered, reached, stays, staysAfterTick) = (false, false, false, false);

        OnTop((overlay, topmost, other) =>
        {
            var reorders = 0;
            WINEVENTPROC counted = (_, _, _, _, _, _, _) => reorders++;
            using var hook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_REORDER, PInvoke.EVENT_OBJECT_REORDER, null, counted, (uint)other.Id, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
            var window = other.Raise("app");
            covered = Above(window, overlay);
            reached = Pump(overlay, () => reorders > 0);
            Pump(overlay, () => false, ticks: 2);
            stays = Above(window, overlay);
            topmost.Raise();
            staysAfterTick = Above(window, overlay);
            GC.KeepAlive(counted);
        });

        Assert.Equal((true, true, true, true), (covered, reached, stays, staysAfterTick));
    }

    // An overlay's window, as the overlay shows it, kept on top, beside another process's windows.
    private static void OnTop(Action<HWND, Topmost, OtherProcess> body) => TestWindows.OnOwnDesktop(desktop =>
    {
        using var bitmap = new Bitmap(60, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(255, 32, 32, 32));
        }

        using var overlay = new LayeredTestWindow(bitmap, new Position(100, 100));
        using var topmost = new Topmost(overlay.Hwnd);
        using var other = new OtherProcess(desktop);
        body(overlay.Hwnd, topmost, other);
    });

    // The window that the overlay owns, its tooltip.
    private static HWND Owned(HWND owner)
    {
        for (var window = PInvoke.GetTopWindow(HWND.Null); !window.IsNull; window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_OWNER) == owner)
            {
                return window;
            }
        }

        return HWND.Null;
    }

    // Whether a comes before b from the top of the desktop's windows.
    private static bool Above(HWND a, HWND b)
    {
        for (var window = PInvoke.GetTopWindow(HWND.Null); !window.IsNull; window = PInvoke.GetWindow(window, GET_WINDOW_CMD.GW_HWNDNEXT))
        {
            if (window == a || window == b)
            {
                return window == a;
            }
        }

        return false;
    }

    // Handles the thread's messages, the hooks' events among them, until done or out of ticks.
    private static bool Pump(HWND window, Func<bool> done, int ticks = Patience)
    {
        PInvoke.SetTimer(window, Waiting, 20, null);
        try
        {
            for (var ticked = 0; ticked < ticks && !done() && PInvoke.GetMessage(out var message, HWND.Null, 0, 0) > 0;)
            {
                ticked += message.message == PInvoke.WM_TIMER ? 1 : 0;
                PInvoke.TranslateMessage(message);
                PInvoke.DispatchMessage(message);
            }
        }
        finally
        {
            TestPInvoke.KillTimer(window, Waiting);
        }

        return done();
    }
}
