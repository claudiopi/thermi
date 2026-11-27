using Thermi.Core.Placement;

namespace Thermi.App.Gestures;

/// <summary>What a press on a tray icon asks: what a click or Enter does, its menu, or what a double-click does.</summary>
internal enum TrayPress
{
    Activate,
    Menu,
    Twice,
}

/// <summary>What the host does for a press on a tray icon: nothing more, paint, the icon's menu, or the window on its item.</summary>
internal enum TrayAnswer
{
    None,
    Paint,
    Menu,
    Settings,
}

/// <summary>
/// What a press on a tray icon does, on every OS: a click on thermi's icon shows the overlays it hid, or else opens its
/// menu; an item's icon opens its overlay's menu on it, and a double-click the window on it.
/// </summary>
internal static class TrayPresses
{
    public static TrayAnswer Of(string? itemId, TrayPress press, OverlayVisibility visibility) => (itemId, press) switch
    {
        (_, TrayPress.Menu) => TrayAnswer.Menu,
        (null, TrayPress.Activate) => visibility.ClickIcon() ? TrayAnswer.Paint : TrayAnswer.Menu,
        (not null, TrayPress.Twice) => TrayAnswer.Settings,
        _ => TrayAnswer.None,
    };
}
