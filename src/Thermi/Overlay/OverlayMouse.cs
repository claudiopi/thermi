using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Windows.Win32;

namespace Thermi.Overlay;

internal enum MouseAction
{
    /// <summary>Not the overlay's: Windows handles it.</summary>
    None,

    /// <summary>A press began: the overlay takes the mouse until it ends.</summary>
    Capture,

    /// <summary>The overlay goes to <see cref="MouseStep.At"/>: where the drag has it, or where it was while still a click.</summary>
    Move,

    /// <summary>The press ended: <see cref="MouseStep.Press"/> is let go, written only if it was a drag.</summary>
    Release,

    /// <summary>Another window took the mouse mid-press, or Esc was down as it moved: the overlay goes back to its place.</summary>
    Lost,

    /// <summary>A double-click: the settings open.</summary>
    Open,
}

internal sealed record MouseStep(MouseAction Action, Press? Press = null, Position? At = null);

/// <summary>
/// What each mouse message does to the overlay. It moves only once the cursor passes the drag distance, and never when
/// locked, so a click or a double-click keeps the snap; a double-click opens the settings, locked or not.
/// </summary>
internal sealed class OverlayMouse
{
    private Press? _press;

    /// <summary>Where a drag in progress has the overlay once past the drag distance, so a repaint leaves it there.</summary>
    public Position? Dragged { get; private set; }

    /// <summary>Ends the press, as Esc does, without a release: false when there was none.</summary>
    public bool Cancel()
    {
        var pressed = _press is not null;
        (_press, Dragged) = (null, null);
        return pressed;
    }

    /// <summary>
    /// The step for <paramref name="message"/>, with the cursor and the overlay's corner where they are now, and whether
    /// Esc was down when it was sent.
    /// </summary>
    public MouseStep On(uint message, Position cursor, Position overlay, DragDistance distance, bool locked, bool escape = false)
    {
        switch (message)
        {
            case PInvoke.WM_LBUTTONDOWN:
                (_press, Dragged) = (new Press(cursor, overlay), null);
                return new MouseStep(MouseAction.Capture);

            // The overlay never has the keyboard's focus: while another app holds Esc as its hotkey, Esc's state is read as each
            // move was sent.
            case PInvoke.WM_MOUSEMOVE when _press is not null && escape:
                (_press, Dragged) = (null, null);
                return new MouseStep(MouseAction.Lost);
            case PInvoke.WM_MOUSEMOVE when _press is { } press:
                Dragged = Snaps.Dragged(press, cursor, distance, locked);
                return new MouseStep(MouseAction.Move, At: Dragged ?? press.Overlay);

            // The press ends here, before the overlay lets the mouse go, so that change of capture is not a lost press.
            case PInvoke.WM_LBUTTONUP when _press is { } press:
                (_press, Dragged) = (null, null);
                return new MouseStep(MouseAction.Release, press);
            case PInvoke.WM_CAPTURECHANGED when _press is not null:
                (_press, Dragged) = (null, null);
                return new MouseStep(MouseAction.Lost);
            case PInvoke.WM_LBUTTONDBLCLK:
                return new MouseStep(MouseAction.Open);
            default:
                return new MouseStep(MouseAction.None);
        }
    }
}
