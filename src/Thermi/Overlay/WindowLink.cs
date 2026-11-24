using Microsoft.Extensions.Logging;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Logging;
using Thermi.Painting;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Kept = Thermi.Core.Look.ItemHistory;
using Sent = Thermi.Core.Messages.ItemHistory;

namespace Thermi.Overlay;

/// <summary>What the link reaches in the host: the settings, the overlays, what lies outside the process, and a paint.</summary>
internal sealed record LinkParts(Settings Settings, Kept History, HostEnvironment Environment, TimeProvider Time, ILogger Log, Action RequestPaint);

/// <summary>
/// The overlay's side of the messages with the window (docs/adr/0009-overlay-and-window-messages.md): whether the window
/// is open, and has been since thermi's first start, what it edits, which marks the overlays while it is open, the ten
/// minutes it asks for as it opens, and what the desktop selects or opens.
/// </summary>
internal sealed class WindowLink(LinkParts parts)
{
    /// <summary>The host's own message that sends the history from its loop, so neither side sends inside the other's handler.</summary>
    public const uint HistoryWanted = PInvoke.WM_APP + 4;

    private static readonly int Slots = (int)SensorHistory.Kept.TotalSeconds;

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
        Opened();
    }

    /// <summary>Looks again whether the window is open: one that closed or crashed leaves no ghost and no mark.</summary>
    public void Opened()
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
                Opened();
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

    /// <summary>Answers the window's question for the history, unless no item has any, where the window starts empty.</summary>
    public void SendHistory()
    {
        if (HistoryOf(parts.Settings.Config, parts.History, parts.Time.GetTimestamp()) is { } history)
        {
            HiddenWindow.Send(HiddenWindow.WindowClass, history);
        }
    }

    /// <summary>A value or an edge clicked or pointed at on the desktop, which only an open window takes.</summary>
    public void Selected(Select select)
    {
        if (WindowOpen)
        {
            Tell(select);
        }
    }

    /// <summary>An open window takes a double-click on a value as a selection; else the window opens, or comes to the front, on the overlay and item.</summary>
    public void OpenWindow(int overlay, string? item)
    {
        parts.Log.OpeningSettings();
        if (parts.Environment.OpenSettings is { } open)
        {
            open(overlay);
        }
        else if (!(item is not null && Tell(new Select(overlay + 1, item, SelectHow.DoubleClick))))
        {
            // A traced overlay starts the window traced, so one trace follows a click to the first frame.
            parts.Environment.StartWindow(overlay, item, parts.Log.IsEnabled(LogLevel.Debug));
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

    /// <summary>
    /// The last ten minutes of every item's meter that read anything, one slot a second ending now, NaN for a gap, or null
    /// when none did: the window then starts with ten empty minutes, as it does when no answer comes.
    /// </summary>
    public static History? HistoryOf(ThermiConfig config, Kept kept, long now)
    {
        var items = new List<Sent>();
        foreach (var item in config.Overlays.SelectMany(overlay => overlay.Items))
        {
            var values = kept.TenMinutes(item.Id);
            if (values.Any(value => value is not null))
            {
                items.Add(new Sent(item.Id, now, SlotsOf(values)));
            }
        }

        return items.Count == 0 ? null : new History(items);
    }

    // A click lets the window take the front. False when no window answers.
    private static bool Tell(Select select) =>
        HiddenWindow.Send(HiddenWindow.WindowClass, select, select.How is SelectHow.Click or SelectHow.DoubleClick ? HiddenWindow.LetTakeTheFront : null);

    // The newest value in the last slot, the slots before the first kept value NaN.
    private static float[] SlotsOf(IReadOnlyList<double?> values)
    {
        var slots = new float[Slots];
        Array.Fill(slots, float.NaN);
        for (var at = 0; at < values.Count; at++)
        {
            slots[Slots - values.Count + at] = values[at] is { } value ? (float)value : float.NaN;
        }

        return slots;
    }
}
