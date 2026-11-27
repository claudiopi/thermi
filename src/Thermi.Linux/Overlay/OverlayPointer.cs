using System.Drawing;
using Thermi.App.Gestures;
using Thermi.App.Overlay;
using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>
/// The pointer on one overlay, as the surfaces tell it: a press holds it, a move past the drag distance drags it, a hold
/// on a pill lifts the pill, the release drops what is held, Esc or another grab puts it back, a click selects in the open
/// window, a double-click opens it, the right button's release opens the menu, and Ctrl and the wheel resize it. What each
/// does is App's <see cref="OverlayPress"/>, as on Windows; this says where the pointer is, moves what it drags, and
/// tells the tooltips, the window and the pointer's shape where it rests.
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
    private PointerShape _shape;
    private bool _menu;

    // A release whose press this overlay took: the press that closes an open menu is the menu's, and its release opens none.
    private bool _rightPressed;

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
        if (pointer.Kind is "press" or "motion")
        {
            MenuClosed();
        }

        Took(pointer, overlay, (cursor, corner), screens);
        Shape(pointer.Kind == "leave" ? PointerShape.Default : _press.Shape(overlay, Drawn, Scale, corner));
    }

    private void Took(ShellPointer pointer, OverlayConfig overlay, (Position Cursor, Position Corner) at, ShellScreens screens)
    {
        var cursor = at.Cursor;
        switch (pointer.Kind)
        {
            case "press" when pointer.Button == 1:
                Pressed(pointer.Time, overlay, at, screens);
                break;
            case "press" when pointer.Button == 3:
                _rightPressed = true;
                break;
            case "release" when pointer.Button == 3 && _rightPressed && _held is null:
                _rightPressed = false;
                Menu(cursor);
                break;
            case "motion" when _held is { } press:
                _dragged = Snaps.Dragged(press, cursor, Distance(screens), overlay.Locked);
                _press.Move(_dragged ?? press.Overlay, _dragged);
                break;
            case "motion":
                Rests(cursor);
                break;
            case "release" when _held is { } press && pointer.Button == 1:
                Released(press, overlay, cursor, pointer.Time, screens);
                break;
            case "scroll":
                _ = _press.Wheel((pointer.Modifiers & Control) != 0, (int)Math.Round(-pointer.Dy * OverlaySizes.Notch), screens.Screens);
                break;
            case "leave":
                Left();
                break;
            case "cancel":
                Cancel();
                break;
            default:
                break;
        }
    }

    /// <summary>Esc, or another grab taking the pointer, while the overlay is held: the drag ends where it began, writing nothing.</summary>
    public void Cancel()
    {
        if (_held is null)
        {
            return;
        }

        _press.Cancel();
        Let();
        _overlay.Tips.Quiet(Hush.Press, quiet: false);
        _parts.Dropped(false);
    }

    /// <summary>Ends the press, whatever it did.</summary>
    public void Let()
    {
        (_held, _dragged) = (null, null);
        _press.Let();
    }

    /// <summary>The menu the overlay opened closed, its row chosen or not: its tooltips may show again.</summary>
    public void MenuClosed()
    {
        if (_menu)
        {
            _menu = false;
            _overlay.Tips.Quiet(Hush.Menu, quiet: false);
        }
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

    private Size Drawn => new(_overlay.Size.Width, _overlay.Size.Height);

    // A second press soon after the first, where it was, is a double-click, which opens the settings but on the corner.
    private void Pressed(uint time, OverlayConfig overlay, (Position Cursor, Position Corner) at, ShellScreens screens)
    {
        if (!Twice(time, at.Cursor, screens))
        {
            Pressed(overlay, at.Cursor, at.Corner, screens);
            _clicked = (time, at.Cursor);
            return;
        }

        Let();
        _clicked = null;
        if (!_press.DoubleClickedCorner(overlay, Drawn, Scale, at.Corner))
        {
            _parts.OpenSettings(_index, ItemAt(at.Cursor), time);
        }
    }

    private void Pressed(OverlayConfig overlay, Position cursor, Position corner, ShellScreens screens)
    {
        Let();
        _overlay.Tips.Quiet(Hush.Press, quiet: true);
        _held = new Press(cursor, corner);
        if (_overlay.Painted is null || _overlay.OnPicture(cursor) is not var (x, y))
        {
            return;
        }

        _press.Press(overlay, Drawn, Scale, screens.Screens, _overlay.Places.Pressed(x, y), corner, carried => new DragShow(_parts, screens, carried switch
        {
            null => null,
            var each => monitor => _overlay.Carried(each.Item, each.Alone, monitor),
        }));
    }

    // A release that moved past the drag distance drops; one that did not is a click, which the open window selects, as
    // Windows' does. A drop leaves the pointer on a value, whose tooltip waits until it leaves or a while passes.
    private void Released(Press press, OverlayConfig overlay, Position cursor, uint time, ShellScreens screens)
    {
        var dragged = Snaps.Dragged(press, cursor, Distance(screens), overlay.Locked) is not null;
        if (!dragged)
        {
            _parts.Channel.Click = time;
            _parts.Window.Selected(new Select(_index + 1, ItemAt(cursor), SelectHow.Click));
        }

        var written = _press.Release(press, Distance(screens));
        (_held, _dragged) = (null, null);
        if (dragged)
        {
            _parts.Quiet.Dropped();
            _overlay.Tips.After(_parts.Quiet);
        }

        _overlay.Tips.Quiet(Hush.Press, quiet: false);
        _parts.Dropped(written);
    }

    // The menu opens as the right button lets go, as Windows' does, beside the overlay; its tooltips wait until it closes.
    private void Menu(Position cursor)
    {
        _menu = true;
        _overlay.Tips.Quiet(Hush.Menu, quiet: true);
        _parts.Menu(_index, cursor, ItemAt(cursor));
    }

    private void Rests(Position cursor)
    {
        if (_overlay.OnPicture(cursor) is var (x, y))
        {
            _overlay.Tips.Point(x, y);
        }

        _parts.Window.Point(_index, ItemAt(cursor));
    }

    private void Left()
    {
        _rightPressed = false;
        _overlay.Tips.Left();
        _parts.Window.Point(_index, null);
    }

    private void Shape(PointerShape shape)
    {
        if (shape != _shape)
        {
            _shape = shape;
            _parts.Surfaces.Cursor((uint)_index, shape);
        }
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
