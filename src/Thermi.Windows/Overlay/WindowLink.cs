using Microsoft.Extensions.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using Thermi.Windows.Logging;
using Thermi.Windows.Painting;
using Thermi.Windows.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Overlay;

/// <summary>What the link reaches in the host: the settings, the overlays, what lies outside the process, and a paint.</summary>
internal sealed record LinkParts(
    Settings Settings, Core.Look.ItemHistory History, HostEnvironment Environment, TimeProvider Time, ILogger Log, Action RequestPaint);

/// <summary>
/// The overlay's side of the messages with the window (docs/adr/0009-overlay-and-window-messages.md): whether the window
/// is open, and has been since thermi's first start, what it edits, which marks the overlays while it is open, the ten
/// minutes it asks for as it opens, and what the desktop selects or opens.
/// </summary>
internal sealed class WindowLink(LinkParts parts)
{
    /// <summary>The host's own message that sends the history from its loop, so neither side sends inside the other's handler.</summary>
    public const uint HistoryWanted = PInvoke.WM_APP + 4;

    // What the window last said it edits; none yet means the first overlay, as the window opens on it.
    private Edited? _edited;

    public bool WindowOpen { get; private set; }

    /// <summary>False from thermi's first start until the window first opens.</summary>
    public bool OpenedOnce { get; private set; } = true;

    /// <summary>Each message from the window, decoded, on the host's thread, after the host has acted on it.</summary>
    public Action<WindowMessage>? Received { get; set; }

    public void Start(bool firstStart)
    {
        OpenedOnce = !firstStart;
        LookForWindow();
        parts.History.WasReset += (_, _) =>
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
        WindowOpen = parts.Environment.WindowOpen();
        OpenedOnce |= WindowOpen;
        if (!WindowOpen)
        {
            _edited = null;
        }
    }

    /// <summary>A message of the window's to <paramref name="host"/>, acted on and then handed to <see cref="Received"/>; null for any other.</summary>
    public LRESULT? FromWindow(HWND host, uint message, nuint wParam, nint lParam)
    {
        var received = HiddenWindow.Receive(message, wParam, lParam, HiddenWindow.WindowClass, out var dropped);
        if (dropped)
        {
            parts.Log.MessageDropped(message);
            return new LRESULT(0);
        }

        switch (received)
        {
            case HistoryAsked:
                PInvoke.PostMessage(host, HistoryWanted, 0, 0);
                break;
            case Edited edited:
                LookForWindow();
                _edited = edited;
                parts.RequestPaint();
                break;
            case null:
                return null;
            default:
                break;
        }

        Received?.Invoke(received);
        return new LRESULT(1);
    }

    /// <summary>Answers the window's question for the history, and tells an open window of a reset.</summary>
    public void SendHistory() => HiddenWindow.Send(HiddenWindow.WindowClass, parts.History.Sent(parts.Settings.Config));

    /// <summary>A value or an edge clicked or pointed at on the desktop, which only an open window takes.</summary>
    public void Selected(Select select)
    {
        if (WindowOpen)
        {
            Tell(select);
        }
    }

    /// <summary>
    /// An open window takes a double-click on a value as a selection; when none takes it, as none runs or one closing or
    /// still starting answers without taking it, the window opens, or comes to the front, on the overlay and item.
    /// </summary>
    public void OpenWindow(int overlay, string? item) => Open(overlay, item, item is null ? null : SelectHow.DoubleClick);

    /// <summary>"Custom…" of an overlay's Size menu: the open window goes to its Size row, or the window opens there.</summary>
    public void OpenSize(int overlay) => Open(overlay, null, SelectHow.Size);

    private void Open(int overlay, string? item, SelectHow? how)
    {
        parts.Log.OpeningSettings();
        if (how is { } asked && Tell(new Select(overlay + 1, item, asked)))
        {
            return;
        }

        if (parts.Environment.OpenSettings is { } open)
        {
            open(overlay);
        }
        else
        {
            // A traced overlay starts the window traced, so one trace follows a click to the first frame.
            parts.Environment.StartWindow(overlay, item, parts.Log.IsEnabled(LogLevel.Debug), sizeRow: how == SelectHow.Size);
        }
    }

    /// <summary>The place of the overlay the open window edits, or null while it is closed or edits none.</summary>
    public int? Editing(int count) =>
        !WindowOpen ? null
        : _edited is null ? 0
        : _edited.Overlay is { } overlay && overlay >= 1 && overlay <= count ? overlay - 1
        : null;

    /// <summary>
    /// How the open window marks an overlay: the one it edits shows its selected and pointed values; every other fades its
    /// frame, or whole when its pills are the opposite of the taskbar's.
    /// </summary>
    public EditMarks Marks(int overlay, int? editing, OverlayConfig config, bool lightTaskbar, Rgb accent)
    {
        if (editing is null)
        {
            return EditMarks.None;
        }

        if (overlay == editing)
        {
            return new EditMarks(Fade.None, _edited?.Selected, _edited?.Pointed, accent);
        }

        var opposite = config.Style.Pills == (lightTaskbar ? PillTheme.Dark : PillTheme.Light);
        return new EditMarks(opposite ? Fade.Whole : Fade.Frame, null, null, accent);
    }

    // A click lets the window take the front. False when no window answers.
    private static bool Tell(Select select) =>
        HiddenWindow.Send(
            HiddenWindow.WindowClass,
            select,
            select.How is SelectHow.Click or SelectHow.DoubleClick or SelectHow.Size ? HiddenWindow.LetTakeTheFront : null);
}
