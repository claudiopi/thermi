using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.App.Gestures;

/// <summary>What a button of one of thermi's notices does, on every OS: what it says, on what the notice is about.</summary>
internal static class NoticeChoices
{
    /// <summary>
    /// A click on a notice that has no buttons of its own, as Windows' from an icon: its first button's, and the first
    /// run's, which has none, opens the window.
    /// </summary>
    public static NoticeButtons Clicked(Notice notice) => (NoticeButtons)((int)notice.Buttons & -(int)notice.Buttons);

    /// <summary>
    /// Does what <paramref name="button"/> of <paramref name="notice"/> says: the hidden overlays show, the overlay hidden on
    /// its own shows where it was, click-through goes off, or the first run opens the window; "Got it" does nothing. A
    /// notice that is no longer the one <paramref name="showing"/> does nothing either, as on Windows, where it is gone;
    /// the first run's opens the window all the same. True when it changed anything.
    /// </summary>
    public static bool Choose(
        Notice notice,
        NoticeButtons button,
        Notice? showing,
        Settings settings,
        OverlayVisibility visibility,
        Func<ThermiConfig, ThermiConfig, EditResult> settle,
        Action openSettings)
    {
        var kind = notice.Kind;
        if (notice != showing && kind is not NoticeKind.FirstRun and not NoticeKind.FirstRunDriver)
        {
            return false;
        }

        if (kind is NoticeKind.FirstRun or NoticeKind.FirstRunDriver)
        {
            openSettings();
        }
        else if (kind == NoticeKind.OverlaysHidden && button is NoticeButtons.ShowIt or NoticeButtons.ShowThem)
        {
            visibility.HideOverlays = false;
        }
        else if (kind == NoticeKind.OverlayHidden && button == NoticeButtons.ShowIt)
        {
            settings.Edit(config =>
                settle(config, notice.Overlays.Aggregate(config, (current, at) => OverlayEdits.SetHidden(current, at, hidden: false).Config)).Config);
        }
        else if (kind is NoticeKind.ClickThrough or NoticeKind.ClickThroughAll && button == NoticeButtons.Undo)
        {
            settings.Edit(config => Notices.Undo(config, notice));
        }
        else
        {
            return false;
        }

        return true;
    }
}
