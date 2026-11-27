using Microsoft.Extensions.Logging;
using Thermi.App.Link;
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
/// The overlay's side of the messages with the window on Windows (docs/adr/0009-overlay-and-window-messages.md): App's
/// <see cref="OverlayEnd"/> decides what is told, and this carries it through the hidden windows and starts the window.
/// </summary>
internal sealed class WindowLink : IWindowChannel
{
    /// <summary>The host's own message that sends the history from its loop, so neither side sends inside the other's handler.</summary>
    public const uint HistoryWanted = PInvoke.WM_APP + 4;

    private readonly LinkParts _parts;
    private readonly OverlayEnd _end;

    public WindowLink(LinkParts parts)
    {
        _parts = parts;
        _end = new OverlayEnd(this, parts.Settings, parts.History, parts.RequestPaint);
    }

    public bool WindowOpen => _end.WindowOpen;

    /// <summary>False from thermi's first start until the window first opens.</summary>
    public bool OpenedOnce => _end.OpenedOnce;

    /// <summary>Each message from the window, decoded, on the host's thread, after the host has acted on it.</summary>
    public Action<WindowMessage>? Received { get; set; }

    public void Start(bool firstStart) => _end.Start(firstStart);

    /// <summary>Looks again whether the window is open: one that closed or crashed leaves no ghost and no mark.</summary>
    public void LookForWindow() => _end.LookForWindow();

    /// <summary>A message of the window's to <paramref name="host"/>, acted on and then handed to <see cref="Received"/>; null for any other.</summary>
    public LRESULT? FromWindow(HWND host, uint message, nuint wParam, nint lParam)
    {
        var received = HiddenWindow.Receive(message, wParam, lParam, HiddenWindow.WindowClass, out var dropped);
        if (dropped)
        {
            _parts.Log.MessageDropped(message);
            return new LRESULT(0);
        }

        if (received is null)
        {
            return null;
        }

        if (_end.Heard(received))
        {
            PInvoke.PostMessage(host, HistoryWanted, 0, 0);
        }

        Received?.Invoke(received);
        return new LRESULT(1);
    }

    /// <summary>Answers the window's question for the history, and tells an open window of a reset.</summary>
    public void SendHistory() => _end.SendHistory();

    /// <summary>A value or an edge clicked on the desktop, which only an open window takes.</summary>
    public void Selected(Select select) => _end.Selected(select);

    /// <summary>The pointer on a value of an overlay, or off its values, which an open window marks on its row.</summary>
    public void Point(int overlay, string? item) => _end.Point(overlay, item);

    /// <summary>A desktop gesture written, which an open window takes as a step of its own history.</summary>
    public void Gestured(Gesture gesture) => _end.Gestured(gesture);

    /// <summary>A double-click on a value or an edge: the open window selects it, or the window opens on it.</summary>
    public void OpenWindow(int overlay, string? item)
    {
        _parts.Log.OpeningSettings();
        _end.OpenWindow(overlay, item);
    }

    /// <summary>"Custom…" of an overlay's Size menu: the open window goes to its Size row, or the window opens there.</summary>
    public void OpenSize(int overlay)
    {
        _parts.Log.OpeningSettings();
        _end.OpenSize(overlay);
    }

    /// <summary>The place of the overlay the open window edits, or null while it is closed or edits none.</summary>
    public int? Editing(int count) => _end.Editing(count);

    /// <summary>How the open window marks an overlay, as <see cref="OverlayEnd.Marks"/> says, in the accent color.</summary>
    public EditMarks Marks(int overlay, int? editing, OverlayConfig config, bool lightTaskbar, Rgb accent) =>
        _end.Marks(overlay, editing, config, lightTaskbar) is var marks && marks == WindowMarks.None
            ? EditMarks.None
            : new EditMarks(marks.Fade, marks.Selected, marks.Pointed, accent);

    bool IWindowChannel.WindowOpen() => _parts.Environment.WindowOpen();

    // A click lets the window take the front.
    bool IWindowChannel.Send(WindowMessage message, bool front) =>
        HiddenWindow.Send(HiddenWindow.WindowClass, message, front ? HiddenWindow.LetTakeTheFront : null);

    void IWindowChannel.Open(int overlay, string? item, bool sizeRow)
    {
        if (_parts.Environment.OpenSettings is { } open)
        {
            open(overlay);
        }
        else
        {
            // A traced overlay starts the window traced, so one trace follows a click to the first frame.
            _parts.Environment.StartWindow(overlay, item, _parts.Log.IsEnabled(LogLevel.Debug), sizeRow);
        }
    }
}
