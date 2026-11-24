using Thermi.Core.Look;
using Thermi.Overlay;
using Thermi.Ui.Editor;

namespace Thermi.Ui.Web;

/// <summary>
/// The window for as long as it is open, on its own thread: the editor, the bridge, the page and the window, the beacon
/// that the overlay and a second <c>thermi settings</c> find, and the link to the overlay.
/// </summary>
internal static class WindowSession
{
    /// <summary>Shows the window and returns once it has closed; a failure on its thread is thrown here.</summary>
    public static void Run(WindowStart start)
    {
        var turn = new WindowTurn();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(turn);
        try
        {
            var editor = new WindowEditor(start.Settings, turn.Post, start.Environment.Log, start.Time);
            var chart = new ChartHistory(start.Time);
            using var bridge = new WindowBridge(start, editor, chart, turn.Post);
            using var page = new WebPage(bridge, start.Environment.Log);
            using var window = new WebWindow(start, turn, page);
            bridge.Window = window.Hwnd;
            window.DesktopChanged = bridge.DesktopChanged;
            using var link = new OverlayLink(chart, editor, bridge.Pick, bridge.Tell, start.Time);
            bridge.Link = link;
            using var beacon = new SettingsBeacon(_ => window.Raise()) { Received = link.Received };
            void Arrived(object? sender, EventArgs e)
            {
                turn.Post(bridge.Arrived);
            }

            start.Environment.Sensors.Received += Arrived;

            // The window is already open to the overlay, by its mutex: "show" brings every overlay up while it is.
            OverlayHost.AskToShow();
            OverlayLink.AskHistory();
            window.Run();
            start.Environment.Sensors.Received -= Arrived;
            editor.EndGesture();
            editor.Detach();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
