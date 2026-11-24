namespace Thermi.Core.Placement;

/// <summary>
/// An element UI Automation finds on a taskbar: its box, whether it is a button, and its class and automation id, which
/// tell Start, the notification area's buttons and the apps' apart.
/// </summary>
public readonly record struct TaskbarElement(PixelRect Box, bool Button, string ClassName, string AutomationId);

/// <summary>How far a taskbar's buttons reach, Start and the apps' and any other but the notification area's.</summary>
public sealed record TaskbarButtons(PixelRect Extent)
{
    /// <summary>
    /// The buttons among <paramref name="elements"/> that lie on <paramref name="bar"/>, or null when there are none.
    /// The notification area's are left out: on Windows 10 only their window, <paramref name="tray"/>, tells them, and
    /// Windows 11 also names their class "SystemTray.…". Start is a toggle button on Windows 11 and a window of class
    /// "Start" on Windows 10 and its look-alikes, either named StartButton. thermi's own buttons, the settings window's,
    /// named after its program <paramref name="program"/> (thermi.exe), count as if they were not there: they come and go
    /// with the window, and counting them would push an overlay off the taskbar or along it while it is being edited.
    /// The buttons after one slide back by its width, toward Start when the buttons begin at the taskbar's left end, and
    /// from both sides when they are centered.
    /// </summary>
    public static TaskbarButtons? Of(IEnumerable<TaskbarElement> elements, PixelRect bar, PixelRect? tray, string program)
    {
        ArgumentNullException.ThrowIfNull(elements);
        PixelRect? extent = null;
        PixelRect? start = null;
        var own = new List<PixelRect>();
        foreach (var element in elements)
        {
            var box = element.Box;
            var isStart = element.AutomationId == "StartButton" || element.ClassName == "Start";
            var notification = element.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal) || tray?.Contains(box) == true;
            if (!(isStart || element.Button) || notification || box.Width <= 0 || !bar.Contains(box))
            {
                continue;
            }

            if (Own(element, program))
            {
                own.Add(box);
                continue;
            }

            extent = extent is { } some ? new PixelRect(Math.Min(some.Left, box.Left), some.Top, Math.Max(some.Right, box.Right), some.Bottom) : box;
            start = isStart ? box : start;
        }

        return extent is { } all ? new TaskbarButtons(Without(all, own, start, bar)) : null;
    }

    // Only the room the own buttons took inside the run is given back: past its ends they were never in the extent.
    private static PixelRect Without(PixelRect extent, List<PixelRect> own, PixelRect? start, PixelRect bar)
    {
        var inside = own.Where(box => box.Left >= extent.Left && box.Right <= extent.Right).Sum(box => box.Width);
        return start is { } button && button.Left - bar.Left <= button.Width
            ? extent with { Right = extent.Right - inside }
            : extent with { Left = extent.Left + (inside / 2), Right = extent.Right - (inside - (inside / 2)) };
    }

    // An unpinned program's button is named by its path, as "Appid: {known folder}\thermi\thermi.exe".
    private static bool Own(TaskbarElement element, string program) =>
        program.Length > 0
        && element.AutomationId.StartsWith("Appid: ", StringComparison.Ordinal)
        && element.AutomationId.EndsWith('\\' + program, StringComparison.OrdinalIgnoreCase);
}
