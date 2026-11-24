using Thermi.Core.Settings;

namespace Thermi.Core.Placement;

/// <summary>The window in front as the overlay reads it: its class, its bounds, and whether it is maximized or minimized.</summary>
public sealed record ForegroundWindow(string ClassName, PixelRect Bounds, bool Maximized, bool Minimized)
{
    /// <summary>The bounds of the monitor it is on, or null when Windows cannot tell.</summary>
    public PixelRect? Monitor { get; init; }
}

/// <summary>What Windows says the user is doing, as far as the full-screen rule cares.</summary>
public enum NotificationState
{
    Normal,

    /// <summary>An app has the screen in exclusive full-screen Direct3D, as many games do.</summary>
    Direct3DFullScreen,

    /// <summary>The user turned on presentation mode.</summary>
    Presentation,
}

/// <summary>How an overlay shows: not at all, or as it is.</summary>
public enum OverlayShown
{
    Hidden,
    Shown,
}

/// <summary>
/// Whether each overlay shows: unless its own Hide, which is saved, the "Hide overlays" flag, which is not, or its
/// "When to show" hides it. The open window changes nothing here: it shows a hidden overlay in its own map and list, never
/// on the desktop. The flag lives only here, so every start of thermi begins without it. There is no clock of its own: the
/// overlay asks on its once-a-second tick, so it follows a full-screen app within a second. The tray icons never depend on it.
/// </summary>
public sealed class OverlayVisibility
{
    // The desktop, the taskbars, Start, search, Task View and Alt+Tab, some of which cover a whole monitor.
    private static readonly string[] Shell =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging"];

    /// <summary>"Hide overlays": a flag in memory, never saved, that hides every overlay until showing clears it.</summary>
    public bool HideOverlays { get; set; }

    /// <summary>
    /// Whether a full-screen app covers each overlay's monitor: the window in front covers the whole monitor, is no part
    /// of the desktop or the shell, and is neither maximized, which covers it too with an auto-hidden taskbar, nor
    /// minimized. Windows' own answer, <paramref name="state"/>, tells no monitor from another, so an exclusive Direct3D
    /// app or presentation mode counts on the monitor of the window in front, and on every monitor when that monitor
    /// cannot be told: an exclusive app may lose its mode to a window drawn over it, wherever it is.
    /// </summary>
    public static IReadOnlyList<bool> FullScreen(ThermiConfig config, IReadOnlyList<Screen> screens, ForegroundWindow? foreground, NotificationState state)
    {
        var app = foreground is { Maximized: false, Minimized: false } && !Shell.Contains(foreground.ClassName, StringComparer.Ordinal) ? foreground : null;
        var quiet = state is NotificationState.Direct3DFullScreen or NotificationState.Presentation;
        var front = screens.FirstOrDefault(screen => screen.Bounds == foreground?.Monitor);
        return [.. config.Overlays.Select(overlay => Snaps.ScreenOf(overlay, screens) is { } screen
            && ((app is not null && app.Bounds.Contains(screen.Bounds)) || (quiet && (front is null || front == screen))))];
    }

    /// <summary>The hide hotkey: sets "Hide overlays", or clears it when set.</summary>
    public void PressHotkey() => HideOverlays = !HideOverlays;

    /// <summary>A click on thermi's icon: clears "Hide overlays" and gives true when it was set; else gives false, and the menu opens.</summary>
    public bool ClickIcon()
    {
        var wasSet = HideOverlays;
        HideOverlays = false;
        return wasSet;
    }

    /// <summary>How each overlay shows, given whether a full-screen app covers its monitor: as its Hide, the flag and its "When to show" say.</summary>
    public IReadOnlyList<OverlayShown> Decide(ThermiConfig config, IReadOnlyList<bool> fullScreen) =>
        [.. config.Overlays.Select((overlay, at) =>
            !overlay.Hidden && !HideOverlays && ByVisibility(overlay.Visibility, at < fullScreen.Count && fullScreen[at]) ? OverlayShown.Shown : OverlayShown.Hidden)];

    private static bool ByVisibility(Visibility visibility, bool fullScreen) =>
        visibility == Visibility.HideInFullscreen ? !fullScreen : visibility != Visibility.OnlyInFullscreen || fullScreen;
}
