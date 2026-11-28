using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
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

    /// <summary>
    /// The title and text of "Notify me when critical"'s notice of <paramref name="told"/>: its item by name, its value or,
    /// after a full-screen app, its peak, and how long it was critical.
    /// </summary>
    public static (string Title, string Text) Of(ItemNotice told, ThermiConfig config, Snapshot snapshot, Texts texts, IFormatProvider numbers)
    {
        var (item, reading, notice) = told;
        var name = Names.Of(config, item, snapshot, texts);
        var value = ReadingFormatter.Format(reading with { Value = notice.Value }, numbers, units: true, config.Temperature, item.Decimals, texts);
        var length = notice.Length < TimeSpan.FromMinutes(1)
            ? texts.Format(numbers, HostText.Seconds, (int)Math.Round(notice.Length.TotalSeconds))
            : texts.Format(numbers, UnitText.Minutes, (int)Math.Round(notice.Length.TotalMinutes));
        return notice.WhilePlaying
            ? (texts.Format(numbers, HostText.CriticalWhilePlayingTitle, name), texts.Format(numbers, HostText.CriticalWhilePlaying, name, value, length))
            : (texts.Format(numbers, HostText.CriticalTitle, name), texts.Format(numbers, HostText.Critical, value, length));
    }
}
