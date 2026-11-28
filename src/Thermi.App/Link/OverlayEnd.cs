using Thermi.App.SettingsFile;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using ItemHistory = Thermi.Core.Look.ItemHistory;

namespace Thermi.App.Link;

/// <summary>What the overlay's end of the link reaches of the settings window, each OS its own way: Windows' hidden windows, Linux's session bus.</summary>
internal interface IWindowChannel
{
    /// <summary>Whether the settings window runs now; one that closed or crashed runs no more.</summary>
    bool WindowOpen();

    /// <summary>
    /// Sends <paramref name="message"/> to the running window, which may come to the front when <paramref name="front"/>;
    /// false when none took it.
    /// </summary>
    bool Send(WindowMessage message, bool front);

    /// <summary>Opens the window, or brings it in front, on overlay <paramref name="overlay"/>, from 0, and its item, or its Size row.</summary>
    void Open(int overlay, string? item, bool sizeRow);
}

/// <summary>What the open window marks on an overlay: its fade, and on the one it edits the values selected and pointed at.</summary>
internal sealed record WindowMarks(EditFade Fade, string? Selected, string? Pointed)
{
    public static WindowMarks None { get; } = new(EditFade.None, null, null);
}

/// <summary>
/// The overlay's end of its link with the settings window (docs/adr/0009-overlay-and-window-messages.md) on every OS:
/// whether the window is open, and has been since thermi's first start; what it edits, which marks the overlays while it
/// is open; the ten minutes it asks for as it opens; and what the desktop selects, points at, opens or does, which only an
/// open window takes. The channel carries it.
/// </summary>
internal sealed class OverlayEnd(IWindowChannel channel, Settings settings, ItemHistory history, Action requestPaint)
{
    // What the window last said it edits; none yet means the first overlay, as the window opens on it.
    private Edited? _edited;

    // The value the pointer is on in each overlay, by its place, which the open window marks on its row.
    private readonly Dictionary<int, string> _pointed = [];

    public bool WindowOpen { get; private set; }

    /// <summary>False from thermi's first start until the window first opens.</summary>
    public bool OpenedOnce { get; private set; } = true;

    public void Start(bool firstStart)
    {
        OpenedOnce = !firstStart;
        LookForWindow();
        history.WasReset += (_, _) =>
        {
            if (WindowOpen)
            {
                SendHistory();
            }
        };
    }

    /// <summary>Looks again whether the window is open: one that closed or crashed leaves no ghost and no mark.</summary>
    public void LookForWindow()
    {
        WindowOpen = channel.WindowOpen();
        OpenedOnce |= WindowOpen;
        if (!WindowOpen)
        {
            _edited = null;
        }
    }

    /// <summary>
    /// A message from the window, acted on: true when it asks for the ten minutes, which the host sends through
    /// <see cref="SendHistory"/> from its own loop once the window's call has returned, so neither side sends inside the
    /// other's handler.
    /// </summary>
    public bool Heard(WindowMessage message)
    {
        if (message is Edited edited)
        {
            LookForWindow();
            _edited = edited;
            requestPaint();
        }

        return message is HistoryAsked;
    }

    /// <summary>Answers the window's question for the history, and tells an open window of a reset.</summary>
    public void SendHistory() => channel.Send(history.Sent(settings.Config), front: false);

    /// <summary>A value or an edge clicked or pointed at on the desktop, which only an open window takes.</summary>
    public void Selected(Select select)
    {
        if (WindowOpen)
        {
            Tell(select);
        }
    }

    /// <summary>The pointer on <paramref name="item"/> of overlay <paramref name="overlay"/>, from 0, or off its values for null.</summary>
    public void Point(int overlay, string? item)
    {
        var left = _pointed.GetValueOrDefault(overlay);
        if (left == item)
        {
            return;
        }

        if (left is not null)
        {
            Selected(new Select(overlay + 1, left, SelectHow.PointOut));
            _pointed.Remove(overlay);
        }

        if (item is not null)
        {
            Selected(new Select(overlay + 1, item, SelectHow.PointIn));
            _pointed[overlay] = item;
        }
    }

    /// <summary>A desktop gesture written, which an open window takes as a step of its own history.</summary>
    public void Gestured(Gesture gesture)
    {
        if (WindowOpen)
        {
            channel.Send(gesture, front: false);
        }
    }

    /// <summary>
    /// An open window takes a double-click on a value as a selection; when none takes it, as none runs or one closing or
    /// still starting answers without taking it, the window opens, or comes to the front, on the overlay and item.
    /// </summary>
    public void OpenWindow(int overlay, string? item) => Open(overlay, item, item is null ? null : SelectHow.DoubleClick);

    /// <summary>"Custom…" of an overlay's Size menu: the open window goes to its Size row, or the window opens there.</summary>
    public void OpenSize(int overlay) => Open(overlay, null, SelectHow.Size);

    /// <summary>The place of the overlay the open window edits, or null while it is closed or edits none.</summary>
    public int? Editing(int count) =>
        !WindowOpen ? null
        : _edited is null ? 0
        : _edited.Overlay is { } overlay && overlay >= 1 && overlay <= count ? overlay - 1
        : null;

    /// <summary>
    /// How the open window marks overlay <paramref name="overlay"/> while it edits <paramref name="editing"/>: the one it
    /// edits shows its selected and pointed values, and every other fades as Core's <see cref="EditLooks.FadeOf"/> says.
    /// </summary>
    public WindowMarks Marks(int overlay, int? editing, OverlayConfig config, bool lightTaskbar) =>
        editing is null ? WindowMarks.None
        : overlay == editing ? new WindowMarks(EditFade.None, _edited?.Selected, _edited?.Pointed)
        : new WindowMarks(EditLooks.FadeOf(overlay, editing, config, lightTaskbar), null, null);

    private void Open(int overlay, string? item, SelectHow? how)
    {
        if (how is { } asked && Tell(new Select(overlay + 1, item, asked)))
        {
            return;
        }

        channel.Open(overlay, item, sizeRow: how == SelectHow.Size);
    }

    // A click lets the window take the front. False when no window answers.
    private bool Tell(Select select) => channel.Send(select, front: select.How is SelectHow.Click or SelectHow.DoubleClick or SelectHow.Size);
}
