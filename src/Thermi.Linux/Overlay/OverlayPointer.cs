using System.Drawing;
using Thermi.App.Gestures;
using Thermi.Core.Editing;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>
/// The pointer on one overlay, as the surfaces tell it: a press holds it, a move past the drag distance drags it, a hold
/// on a pill lifts the pill, the release drops what is held, Esc puts it back, a double-click opens the settings, and Ctrl
/// and the wheel resize it. What each does is App's <see cref="OverlayPress"/>, as on Windows; this says where the
/// pointer is and moves what it drags.
/// </summary>
internal sealed class OverlayPointer : IPressHost, IDisposable
{
    // Clutter's, GDK's and X11's Control.
    private const uint Control = 1 << 2;

    private readonly int _index;
    private readonly OverlayParts _parts;
    private readonly DesktopOverlay _overlay;
    private readonly OverlayPress _press;
    private Press? _held;
    private Position? _dragged;
    private (uint Time, Position At)? _clicked;
    private ShellScreens? _screens;
    private ITimer? _wake;

    public OverlayPointer(int index, OverlayParts parts, DesktopOverlay overlay)
    {
        (_index, _parts, _overlay) = (index, parts, overlay);
        _press = new OverlayPress(index, parts.Settings, this);
    }

    /// <summary>The drag a press holds, which the overlay is painted as, or null.</summary>
    public DesktopDrag? Drag => _press.Drag;

    /// <summary>Where the overlay goes while a press holds it, or null where it goes as its settings say.</summary>
    public Position? HeldAt => _press.HeldAt(_dragged);

    /// <summary>Whether a drag holds the overlay or is over it, which keeps it whole, without its fade.</summary>
    public bool Holds => _press.Holds;

    public TimeProvider Time => _parts.Time;

    public DropPreview Preview => _parts.Preview;

    public GestureUndo? Gestures => _parts.Gestures;

    /// <summary>What the pointer did on the overlay, at a point of the stage.</summary>
    public void On(ShellPointer pointer, ShellScreens screens)
    {
        _screens = screens;
        var cursor = screens.Thermi(pointer.X, pointer.Y);
        var config = _parts.Settings.Config;
        if (_index >= config.Overlays.Count || _overlay.At is not { } corner)
        {
            return;
        }

        var overlay = config.Overlays[_index];
        switch (pointer.Kind)
        {
            case "press" when pointer.Button == 1 && Twice(pointer.Time, cursor, screens):
                Let();
                _clicked = null;
                if (!_press.DoubleClickedCorner(overlay, new Size(_overlay.Size.Width, _overlay.Size.Height), Scale, corner))
                {
                    _parts.OpenSettings(_index, ItemAt(cursor));
                }

                break;
            case "press" when pointer.Button == 1:
                Pressed(overlay, cursor, corner, screens);
                _clicked = (pointer.Time, cursor);
                break;
            case "press" when pointer.Button == 3:
                _parts.Menu(_index, pointer.X, pointer.Y, ItemAt(cursor));
                break;
            case "motion" when _held is { } press:
                _dragged = Snaps.Dragged(press, cursor, Distance(screens), overlay.Locked);
                _press.Move(_dragged ?? press.Overlay, _dragged);
                break;
            case "release" when _held is { } press && pointer.Button == 1:
                var written = _press.Release(press, Distance(screens));
                (_held, _dragged) = (null, null);
                _parts.Dropped(written);
                break;
            case "scroll":
                _ = _press.Wheel((pointer.Modifiers & Control) != 0, (int)Math.Round(-pointer.Dy * OverlaySizes.Notch), screens.Screens);
                break;
            default:
                break;
        }
    }

    /// <summary>Esc while the overlay is held: the drag ends where it began, and nothing is written.</summary>
    public void Cancel()
    {
        if (_held is not { } press || _screens is not { } screens)
        {
            return;
        }

        Let();
        _overlay.MoveTo(press.Overlay, screens);
        _parts.RequestPaint();
        _parts.Dropped(false);
    }

    /// <summary>Ends the press, whatever it did.</summary>
    public void Let()
    {
        (_held, _dragged) = (null, null);
        _press.Let();
    }

    public void Dispose()
    {
        _press.Dispose();
        _wake?.Dispose();
    }

    Position IPressHost.Cursor() => _parts.Cursor?.At ?? new Position(0, 0);

    void IPressHost.RequestPaint() => _parts.RequestPaint();

    IReadOnlyList<PixelRect?> IPressHost.Shown() => _parts.Shown();

    DropTarget IPressHost.JoinAt(int onto, Position cursor, string? alone) => _parts.JoinAt(onto, cursor, alone);

    EditResult IPressHost.Settle(ThermiConfig before, ThermiConfig after) => _parts.Settle(before, after);

    void IPressHost.Wake(TimeSpan after)
    {
        _wake?.Dispose();
        _wake = _parts.Time.CreateTimer(_ => _parts.Post(_press.Hold), null, after, Timeout.InfiniteTimeSpan);
    }

    void IPressHost.Sleep()
    {
        _wake?.Dispose();
        _wake = null;
    }

    bool IPressHost.HoldEscape()
    {
        _parts.Surfaces.HoldKeys((uint)_index, held: true);
        return true;
    }

    void IPressHost.LetEscape() => _parts.Surfaces.HoldKeys((uint)_index, held: false);

    void IPressHost.MoveTo(Position at)
    {
        if (_screens is { } screens)
        {
            _overlay.MoveTo(at, screens);
        }
    }

    // The overlay's pixels at 100 % to thermi's, as Core's corner and drags measure them.
    private float Scale => _overlay.Painted is { } painted ? (float)(painted.Scale * painted.ToThermi) : 1;

    private void Pressed(OverlayConfig overlay, Position cursor, Position corner, ShellScreens screens)
    {
        Let();
        _held = new Press(cursor, corner);
        if (_overlay.Painted is null || _overlay.OnPicture(cursor) is not var (x, y))
        {
            return;
        }

        var size = new Size(_overlay.Size.Width, _overlay.Size.Height);
        _press.Press(overlay, size, Scale, screens.Screens, _overlay.Places.Pressed(x, y), corner, carried => new DragShow(_parts, screens, carried switch
        {
            null => null,
            var each => () => _overlay.Carried(each.Item, each.Alone),
        }));
    }

    // A second press soon after the first, where it was: a double-click, which opens the settings.
    private bool Twice(uint time, Position cursor, ShellScreens screens)
    {
        var near = Distance(screens);
        return _clicked is var (first, at) && time - first <= _parts.Pointer.DoubleClick
            && Math.Abs(cursor.X - at.X) <= near.Width && Math.Abs(cursor.Y - at.Y) <= near.Height;
    }

    private DragDistance Distance(ShellScreens screens)
    {
        var pixels = (int)Math.Round(_parts.Pointer.DragThreshold * screens.Scale);
        return new(pixels, pixels);
    }

    private string? ItemAt(Position cursor) => _overlay.OnPicture(cursor) is var (x, y) ? _overlay.Places.At(x, y)?.Id : null;
}
