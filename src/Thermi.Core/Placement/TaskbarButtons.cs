namespace Thermi.Core.Placement;

/// <summary>
/// An element UI Automation finds on a taskbar: its box, whether it is a button, and its class and automation id, which
/// tell Start, the notification area's buttons and the apps' apart.
/// </summary>
public readonly record struct TaskbarElement(PixelRect Box, bool Button, string ClassName, string AutomationId);

/// <summary>How far a taskbar's buttons reach, Start and the apps' and any other but the notification area's, and Start's own box when it was among them.</summary>
public sealed record TaskbarButtons(PixelRect Extent, PixelRect? Start)
{
    /// <summary>
    /// The buttons among <paramref name="elements"/> that lie on <paramref name="bar"/>, or null when there are none.
    /// The notification area's are left out: on Windows 10 only their window, <paramref name="tray"/>, tells them, and
    /// Windows 11 also names their class "SystemTray.…". Start is a toggle button on Windows 11 and a window of class
    /// "Start" on Windows 10 and its look-alikes, either named StartButton. thermi's own button, the settings window's,
    /// named after its program <paramref name="program"/> (thermi.exe), is left out too: it comes and goes with the window,
    /// and counting it would push an overlay off the taskbar while it is being edited. Pinned, it sits among the others,
    /// whose extent it seldom changes.
    /// </summary>
    public static TaskbarButtons? Of(IEnumerable<TaskbarElement> elements, PixelRect bar, PixelRect? tray, string program)
    {
        ArgumentNullException.ThrowIfNull(elements);
        PixelRect? extent = null;
        PixelRect? start = null;
        foreach (var element in elements)
        {
            var box = element.Box;
            var isStart = element.AutomationId == "StartButton" || element.ClassName == "Start";
            var notification = element.ClassName.StartsWith("SystemTray.", StringComparison.Ordinal) || tray?.Contains(box) == true;
            if ((isStart || element.Button) && !notification && !Own(element, program) && box.Width > 0 && bar.Contains(box))
            {
                extent = extent is { } some ? new PixelRect(Math.Min(some.Left, box.Left), some.Top, Math.Max(some.Right, box.Right), some.Bottom) : box;
                start = isStart ? box : start;
            }
        }

        return extent is { } all ? new TaskbarButtons(all, start) : null;
    }

    // An unpinned program's button is named by its path, as "Appid: {known folder}\thermi\thermi.exe".
    private static bool Own(TaskbarElement element, string program) =>
        program.Length > 0 && element.AutomationId.StartsWith("Appid: ", StringComparison.Ordinal) && element.AutomationId.EndsWith('\\' + program, StringComparison.OrdinalIgnoreCase);
}
