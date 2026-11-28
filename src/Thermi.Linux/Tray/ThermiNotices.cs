using System.Globalization;
using Thermi.App.Gestures;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Tray;

/// <summary>What the notices reach in their host: the settings, the overlays' visibility, the format of numbers and the window.</summary>
internal sealed record NoticeHost(Settings Settings, OverlayVisibility Visibility, CultureInfo Culture, Action RequestPaint)
{
    /// <summary>A notice's edit, with the overlays it pushed along a taskbar keeping their places, or refused.</summary>
    public required Func<ThermiConfig, ThermiConfig, EditResult> Settle { get; init; }

    /// <summary>Opens the settings window, as the first start's notice does.</summary>
    public required Action OpenSettings { get; init; }
}

/// <summary>
/// thermi's notices as the desktop's own notifications, each once, whose buttons do what they do on Windows, through
/// <see cref="NoticeChoices"/>: Core's notices of the first start, of hidden overlays and of click-through; "Notify me when
/// critical"'s, after its 10 s; that the settings window ended with an error; and that no overlay can show on this desktop.
/// Each button is known by its action id, and a click on one of Core's notices itself does what its first button does.
/// </summary>
internal sealed class ThermiNotices
{
    public const string FirstRun = "first-run";
    public const string ShowIt = "show-it";
    public const string ShowThem = "show-them";
    public const string Undo = "undo";
    public const string GotIt = "got-it";
    public const string Critical = "critical";
    public const string OpenAgain = "open-again";

    /// <summary>A click on the notice itself, past its buttons, as the freedesktop notifications name it.</summary>
    public const string Body = "default";

    private static readonly (NoticeButtons Button, string Action, HostText Label)[] Buttons =
    [
        (NoticeButtons.ShowIt, ShowIt, HostText.NoticeShowIt),
        (NoticeButtons.ShowThem, ShowThem, HostText.NoticeShowThem),
        (NoticeButtons.Undo, Undo, HostText.NoticeUndo),
        (NoticeButtons.GotIt, GotIt, HostText.NoticeGotIt),
    ];

    private readonly DesktopNotices _desktop;
    private readonly NoticeHost _host;
    private readonly Notices _notices = new();
    private readonly NotifyMe _notifyMe;
    private readonly Dictionary<uint, Notice> _told = [];
    private bool _firstStart;

    /// <summary>Notices on <paramref name="desktop"/>; <paramref name="firstStart"/> when thermi found no settings as it started.</summary>
    public ThermiNotices(DesktopNotices desktop, NoticeHost host, TimeProvider time, bool firstStart)
    {
        (_desktop, _host, _firstStart) = (desktop, host, firstStart);
        _notifyMe = new NotifyMe(time);
        desktop.Invoked += Clicked;
    }

    private Texts Texts => Texts.For(_host.Settings.Config.Language, CultureInfo.CurrentUICulture);

    /// <summary>The window opened: thermi's first start is over.</summary>
    public void WindowOpened() => _firstStart = false;

    /// <summary>The settings window could not start, or ended with an error: a notice says where its log is, and opens it again.</summary>
    public void WindowFailed()
    {
        var texts = Texts;
        _ = _desktop.Say(
            texts.Of(HostText.WindowFailedTitle),
            texts.Format(_host.Culture, HostText.WindowFailed, ThermiLog.WindowPath),
            (OpenAgain, texts.Of(HostText.NoticeOpenSettings)));
    }

    /// <summary>Takes in the settings and the hide flag after each paint, and tells the notice that is new, if one is.</summary>
    public void Update(ThermiConfig config)
    {
        if (_notices.Update(new NoticeInput(config, _host.Visibility.HideOverlays, FromWindow: false, _firstStart)) is not { } notice)
        {
            return;
        }

        var texts = Texts;
        var (title, text) = NoticeTexts.Of(notice, config, texts, _host.Culture);
        (string, string)[] buttons = notice.Kind is NoticeKind.FirstRun or NoticeKind.FirstRunDriver
            ? [(FirstRun, texts.Of(HostText.NoticeOpenSettings))]
            : [.. Buttons.Where(each => notice.Buttons.HasFlag(each.Button)).Select(each => (each.Action, texts.Of(each.Label)))];
        // A click on the notice does what its first button does, as on Windows; a server shows no button for it.
        _ = Tell(notice, title, text, [.. buttons, (Body, "")]);
    }

    /// <summary>"Notify me when critical": a fresh snapshot, once a second, and the notice of each item critical long enough.</summary>
    public void Readings(ThermiConfig config, Snapshot snapshot, bool fullScreen)
    {
        var texts = Texts;
        foreach (var told in _notifyMe.Add(config, snapshot, fullScreen))
        {
            var (title, text) = NoticeTexts.Of(told, config, snapshot, texts, _host.Culture);
            _ = _desktop.Say(title, text, (Critical, texts.Of(HostText.NoticeGotIt)));
        }
    }

    private async Task Tell(Notice notice, string title, string text, (string, string)[] buttons)
    {
        if (await _desktop.Say(title, text, buttons) is { } id)
        {
            _told[id] = notice;
        }
    }

    private void Clicked(uint id, string action)
    {
        if (action == OpenAgain)
        {
            _host.OpenSettings();
            return;
        }

        if (!_told.Remove(id, out var notice))
        {
            return;
        }

        var button = action == Body ? NoticeChoices.Clicked(notice) : Buttons.FirstOrDefault(each => each.Action == action).Button;
        if (NoticeChoices.Choose(notice, button, _notices.Showing, _host.Settings, _host.Visibility, _host.Settle, Open))
        {
            _host.RequestPaint();
        }
    }

    private void Open()
    {
        WindowOpened();
        _host.OpenSettings();
    }
}
