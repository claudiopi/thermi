using Thermi.Core.Editing;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Overlay;

/// <summary>
/// Core's notices of the overlays shown as the desktop's own, whose buttons do what they say: the overlays hidden show
/// again, click-through goes off, or the notice just closes.
/// </summary>
internal sealed class OverlayNotices
{
    private const string ShowIt = "show-it";
    private const string ShowThem = "show-them";
    private const string Undo = "undo";
    private const string GotIt = "got-it";

    private readonly OverlayParts _parts;
    private readonly DesktopNotices _desktop;
    private readonly Func<Texts> _texts;
    private readonly Notices _notices = new();

    public OverlayNotices(OverlayParts parts, DesktopNotices desktop, Func<Texts> texts)
    {
        (_parts, _desktop, _texts) = (parts, desktop, texts);
        desktop.Invoked += Clicked;
    }

    /// <summary>Takes in the settings and the hide flag after each paint, and tells the notice that is new, if one is.</summary>
    public void Update(ThermiConfig config)
    {
        if (_notices.Update(new NoticeInput(config, _parts.Visibility.HideOverlays, FromWindow: false, FirstStart: false)) is not { } notice)
        {
            return;
        }

        var texts = _texts();
        var (title, text) = NoticeTexts.Of(notice, config, texts, _parts.Culture);
        (string, string)[] buttons =
        [
            .. notice.Buttons.HasFlag(NoticeButtons.ShowIt) ? [(ShowIt, texts.Of(HostText.NoticeShowIt))] : Array.Empty<(string, string)>(),
            .. notice.Buttons.HasFlag(NoticeButtons.ShowThem) ? [(ShowThem, texts.Of(HostText.NoticeShowThem))] : Array.Empty<(string, string)>(),
            .. notice.Buttons.HasFlag(NoticeButtons.Undo) ? [(Undo, texts.Of(HostText.NoticeUndo))] : Array.Empty<(string, string)>(),
            .. notice.Buttons.HasFlag(NoticeButtons.GotIt) ? [(GotIt, texts.Of(HostText.NoticeGotIt))] : Array.Empty<(string, string)>(),
        ];
        _ = _desktop.Say(title, text, buttons);
    }

    // A button of a notice no longer showing does nothing: what it was about ended already.
    private void Clicked(string action)
    {
        if (_notices.Showing is not { } notice)
        {
            return;
        }

        if (action is ShowIt or ShowThem && notice.Kind == NoticeKind.OverlaysHidden)
        {
            _parts.Visibility.HideOverlays = false;
            _parts.RequestPaint();
        }
        else if (action is ShowIt && notice.Kind == NoticeKind.OverlayHidden)
        {
            _parts.Settings.Edit(config => _parts.Settle(
                config, notice.Overlays.Aggregate(config, (current, at) => OverlayEdits.SetHidden(current, at, hidden: false).Config)).Config);
        }
        else if (action is Undo && notice.Kind is NoticeKind.ClickThrough or NoticeKind.ClickThroughAll)
        {
            _parts.Settings.Edit(config => Notices.Undo(config, notice));
        }
    }
}
