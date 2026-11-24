using System.Runtime.InteropServices;
using Thermi.Core.Placement;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Thermi.Windows;

/// <summary>
/// How far a taskbar's buttons reach, Start and the apps' buttons and any other but the notification area's, read
/// through UI Automation, since Windows 11 draws them all inside one window. Asking crosses into Explorer and waits on
/// it, so the answer is read off the window's thread and kept per taskbar, read again a while after, as apps open and
/// close; the window hears when it changes.
/// </summary>
/// <param name="time">The clock the answers are kept by.</param>
/// <param name="onTurn">Runs an action on the window's thread, where the answers are kept.</param>
/// <param name="changed">Told on the window's thread when a taskbar's answer changed.</param>
internal sealed class TaskbarApps(TimeProvider time, Action<Action> onTurn, Action changed)
{
    // Often enough to follow an app opening, rarely enough to cost nothing at a paint a second.
    private static readonly TimeSpan Kept = TimeSpan.FromSeconds(2);

    // A busy or hung Explorer gives up the read within this, instead of the minutes UI Automation waits by default.
    private const uint TimeoutMs = 500;

    private readonly Dictionary<PixelRect, (Found? Buttons, long At)> _read = [];
    private readonly HashSet<PixelRect> _reading = [];

    // One client for every read, made on the first; UI Automation's client may be used from any thread of the pool.
    private readonly Lazy<IUIAutomation2> _automation = new(Client);

    /// <summary>
    /// The buttons' extent on the taskbar that lies at <paramref name="bar"/> as last read, or null when it cannot be read
    /// or has not been yet. An answer older than a while is read again in the background.
    /// </summary>
    public PixelRect? Extent(PixelRect bar) => Known(bar)?.Extent;

    /// <summary>The buttons' extent when they begin at the taskbar's left end, Start first, as <see cref="Snaps.LeftButtons"/> tells; null otherwise or unread.</summary>
    public PixelRect? LeftButtons(PixelRect bar) => Known(bar) is { } found ? Snaps.LeftButtons(bar, found.Extent, found.Start) : null;

    private Found? Known(PixelRect bar)
    {
        var known = _read.TryGetValue(bar, out var read);
        if ((!known || time.GetElapsedTime(read.At) >= Kept) && _reading.Add(bar))
        {
            _ = ReadAsync(bar);
        }

        return known ? read.Buttons : null;
    }

    private static IUIAutomation2 Client()
    {
        var automation = (IUIAutomation2)new CUIAutomation8();
        automation.ConnectionTimeout = TimeoutMs;
        automation.TransactionTimeout = TimeoutMs;
        return automation;
    }

    private async Task ReadAsync(PixelRect bar)
    {
        var extent = await Task.Run(() => Read(bar));
        onTurn(() => Keep(bar, extent));
    }

    private void Keep(PixelRect bar, Found? buttons)
    {
        _reading.Remove(bar);
        var same = _read.TryGetValue(bar, out var before) && before.Buttons == buttons;
        _read[bar] = (buttons, time.GetTimestamp());
        if (!same)
        {
            changed();
        }
    }

    private Found? Read(PixelRect bar)
    {
        if (Desktop.TaskbarWindows().FirstOrDefault(each => each.Bounds == bar) is not { Bar.IsNull: false } taskbar)
        {
            return null;
        }

        try
        {
            return Buttons(_automation.Value, taskbar.Bar, bar, taskbar.Tray);
        }
        catch (COMException)
        {
            return null;
        }
    }

    // The notification area's buttons sit in its own window, the only way to tell them on Windows 10; Windows 11 also
    // names their class "SystemTray.…", which holds where its window does not cover them all.
    // Start is a toggle button on Windows 11 and a window of class "Start" on Windows 10 and its look-alikes, either named
    // StartButton by its automation id.
    private static Found? Buttons(IUIAutomation2 automation, HWND window, PixelRect bar, PixelRect? tray)
    {
        var found = automation.ElementFromHandle(window).FindAll(TreeScope.TreeScope_Descendants, automation.CreateTrueCondition());
        PixelRect? extent = null;
        PixelRect? start = null;
        for (var at = 0; at < found.Length; at++)
        {
            var element = found.GetElement(at);
            var rect = element.CurrentBoundingRectangle;
            var box = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
            var type = element.CurrentControlType;
            var className = element.CurrentClassName.ToString();
            var isStart = element.CurrentAutomationId.ToString() == "StartButton" || className == "Start";
            var notification = className.StartsWith("SystemTray.", StringComparison.Ordinal) || tray?.Contains(box) == true;
            if ((isStart || type == UIA_CONTROLTYPE_ID.UIA_ButtonControlTypeId) && !notification && box.Width > 0 && bar.Contains(box))
            {
                extent = extent is { } some ? new PixelRect(Math.Min(some.Left, box.Left), some.Top, Math.Max(some.Right, box.Right), some.Bottom) : box;
                start = isStart ? box : start;
            }
        }

        return extent is { } all ? new Found(all, start) : null;
    }

    /// <summary>What a read found: every button's extent, and Start's own box when it was among them.</summary>
    private sealed record Found(PixelRect Extent, PixelRect? Start);
}
