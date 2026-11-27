using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>What a notice of thermi's says, on every OS: its title and its text, from the translations.</summary>
public static class NoticeTexts
{
    /// <summary>The title and text of <paramref name="notice"/>, which names the toggle hotkey when the settings hold one.</summary>
    public static (string Title, string Text) Of(Notice notice, ThermiConfig config, Texts texts, IFormatProvider numbers)
    {
        var hotkey = config.Hotkeys?.Toggle;
        var one = notice.Overlays.Count == 1 ? MenuTexts.NameOf(config, notice.Overlays[0], texts) : "";
        return notice.Kind switch
        {
            NoticeKind.FirstRun => (texts.Of(HostText.FirstRunTitle), texts.Of(HostText.FirstRun)),
            NoticeKind.FirstRunDriver => (texts.Of(HostText.FirstRunTitle), texts.Of(HostText.FirstRunDriver)),
            NoticeKind.OverlaysHidden when config.Overlays.Count == 1 => (texts.Format(numbers, HostText.OverlayHiddenTitle, one),
                hotkey is null ? texts.Of(HostText.OverlayHiddenByFlag) : texts.Format(numbers, HostText.OverlayHiddenByFlagHotkey, hotkey)),
            NoticeKind.OverlaysHidden => (texts.Of(HostText.OverlaysHiddenTitle),
                hotkey is null ? texts.Of(HostText.OverlaysHidden) : texts.Format(numbers, HostText.OverlaysHiddenHotkey, hotkey)),
            NoticeKind.OverlayHidden => (texts.Format(numbers, HostText.OverlayHiddenTitle, one), texts.Of(HostText.OverlayHidden)),
            NoticeKind.ClickThrough or NoticeKind.ClickThroughAll => (texts.Of(HostText.ClickThroughOnTitle), texts.Of(HostText.ClickThroughOnText)),
            _ => throw new ArgumentOutOfRangeException(nameof(notice), notice.Kind, null),
        };
    }
}
