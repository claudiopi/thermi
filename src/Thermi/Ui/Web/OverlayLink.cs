using Thermi.Core;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Ui.Editor;
using Thermi.Windows;

namespace Thermi.Ui.Web;

/// <summary>
/// The window's side of the messages with the overlay (<c>docs/adr/0009-overlay-and-window-messages.md</c>): it asks for
/// the ten minutes as it opens and takes them for its charts, hands what the desktop selects or points at to the page,
/// takes each desktop gesture as a step of its own history, and tells the overlay what it edits, selects and points at,
/// through Core's throttle and off the window's thread, so the overlay marks it.
/// </summary>
/// <param name="chart">Where the overlay's ten minutes go.</param>
/// <param name="editor">Whose history takes each desktop gesture.</param>
/// <param name="picked">Hands the page what the desktop picked, on the window's thread.</param>
/// <param name="changed">Tells the page that what it reads changed, as the history coming does.</param>
/// <param name="time">The throttle's clock.</param>
internal sealed class OverlayLink(ChartHistory chart, WindowEditor editor, Action<DesktopPickEventArgs> picked, Action changed, TimeProvider time) : IDisposable
{
    private static readonly TimeSpan EditedEvery = TimeSpan.FromMilliseconds(100);

    private readonly Throttle<Edited> _edited = new(EditedEvery, edited => _ = Task.Run(() => HiddenWindow.Send(HiddenWindow.HostClass, edited)), time);

    /// <summary>Asks the overlay for its ten minutes, which come to the beacon; with none in 500 ms the charts start with gaps.</summary>
    public static void AskHistory() => HiddenWindow.Send(HiddenWindow.HostClass, new HistoryAsked());

    /// <summary>A message the beacon took from the overlay or a second thermi, on the window's thread.</summary>
    public void Received(WindowMessage message)
    {
        switch (message)
        {
            case History history:
                chart.Take(history);
                changed();
                break;
            case Select select:
                picked(new DesktopPickEventArgs(select.Overlay - 1, select.ItemId, select.How));
                break;
            case Gesture gesture:
                editor.TakeGesture(gesture.Before, gesture.After);
                break;
            case Raise { Overlay: > 0 } raise:
                picked(new DesktopPickEventArgs(raise.Overlay - 1, raise.ItemId, SelectHow.Click));
                break;
            default:
                break;
        }
    }

    /// <summary>The overlay the page edits, counted from 0, its selected item and the item pointed at, each or null.</summary>
    public void Editing(int? overlay, string? selected, string? pointed) => _edited.Post(new Edited(overlay + 1, selected, pointed));

    public void Dispose() => _edited.Dispose();
}
