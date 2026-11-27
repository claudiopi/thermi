using Thermi.App.Gestures;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Tray;

/// <summary>
/// A tray icon's clicks as Windows' tray tells them: one, or with the one before on the same icon within the person's
/// double-click time, a double-click, as no Linux tray tells it.
/// </summary>
internal sealed class TrayClicks(PointerSettings pointer)
{
    private (string Name, uint Time)? _last;

    /// <summary>The click on the icon of <paramref name="name"/> at <paramref name="time"/>, in milliseconds.</summary>
    public TrayPress Click(string name, uint time)
    {
        var twice = _last is var (last, at) && last == name && time - at <= pointer.DoubleClick;
        _last = twice ? null : (name, time);
        return twice ? TrayPress.Twice : TrayPress.Activate;
    }
}
