using Thermi.Linux.Gnome;

namespace Thermi.Linux.Desktop;

/// <summary>
/// The pointer and Esc on a <see cref="PictureWindow"/>, each event by the kind the extension names it, in the stage's
/// logical pixels from where the window's corner is. A press that another grab takes, the compositor's own or another
/// program's, is lost, as GNOME's <c>cancel</c> tells it: GTK says a grab broke, or X11 marks a leave as a grab's; on
/// Wayland, which keeps the pointer on the surface a button holds, any leave while one does. A touchpad's turn, which
/// Wayland gives in the surface's pixels, is told in notches.
/// </summary>
internal sealed class PicturePointer
{
    private const uint Escape = 0xff1b;

    // Clutter counts a notch for 10 of libinput's pixels, so a touchpad turns an overlay as far here as on GNOME.
    private const double PixelsPerNotch = 10;

    // The pointer's events as the extension names them.
    private static readonly Dictionary<Gdk.EventType, string> Kinds = new()
    {
        [Gdk.EventType.ButtonPress] = "press",
        [Gdk.EventType.ButtonRelease] = "release",
        [Gdk.EventType.MotionNotify] = "motion",
        [Gdk.EventType.Scroll] = "scroll",
    };

    private readonly uint _id;
    private readonly Func<(double X, double Y)> _origin;
    private readonly bool _leaveLoses;
    // Where on the stage the pointer last was, as the last event that told a point said.
    private (double X, double Y) _stage;
    private bool _pressed;

    /// <summary>
    /// The pointer on <paramref name="window"/>, whose corner is at <paramref name="origin"/> of the stage; a leave during a
    /// press loses it where <paramref name="leaveLoses"/>, as on Wayland.
    /// </summary>
    public PicturePointer(uint id, Gtk.Window window, Func<(double X, double Y)> origin, bool leaveLoses)
    {
        (_id, _origin, _leaveLoses) = (id, origin, leaveLoses);
        var events = Gtk.EventControllerLegacy.New();
        // GirCore reads no GdkEvent from a signal's arguments, only from the controller.
        events.OnEvent += (controller, _) => controller.GetCurrentEvent() is { } heard && Heard(heard);
        window.AddController(events);
        // GTK tells a pointer's coming and going only to a motion controller: where it came is its first point on the window.
        var crossing = Gtk.EventControllerMotion.New();
        crossing.OnEnter += (controller, args) => Crossed(controller, "motion", Told(args.X, args.Y));
        crossing.OnLeave += (controller, _) =>
        {
            Crossed(controller, "leave", _stage);
            if (_leaveLoses || (controller.GetCurrentEvent() is { } left && Gdk.Internal.CrossingEvent.GetMode(left.Handle) == Gdk.CrossingMode.Grab))
            {
                Lose();
            }
        };
        window.AddController(crossing);
    }

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    public Action<uint, string> Key { get; set; } = (_, _) => { };

    /// <summary>A press on the window lost the pointer to another grab, by the window's picture's id.</summary>
    public Action<uint> Lost { get; set; } = _ => { };

    private void Lose()
    {
        if (_pressed)
        {
            _pressed = false;
            Lost(_id);
        }
    }

    private void Crossed(Gtk.EventController crossing, string kind, (double X, double Y) at) =>
        Pointer(new ShellPointer(_id, kind, at.X, at.Y, 0, (uint)crossing.GetCurrentEventState(), crossing.GetCurrentEventTime(), 0, 0));

    // A point on the window, told on the stage from where the window's corner is now, and kept as the pointer's last.
    private (double X, double Y) Told(double x, double y)
    {
        var (left, top) = _origin();
        return _stage = (left + x, top + y);
    }

    private bool Heard(Gdk.Event heard)
    {
        // GirCore gives every event as a Gdk.Event, never as its kind's class, so each kind's details are read through
        // that kind's calls on the event's handle.
        var type = heard.GetEventType();
        if (type == Gdk.EventType.KeyPress && Gdk.Internal.KeyEvent.GetKeyval(heard.Handle) == Escape)
        {
            Key(_id, "Escape");
            return true;
        }

        if (type == Gdk.EventType.GrabBroken)
        {
            Lose();
            return false;
        }

        if (!Kinds.TryGetValue(type, out var kind))
        {
            return false;
        }

        // Wayland tells a button and a turn of the wheel no point: GTK gives the last motion's on the window, which a window
        // moved since, under a pointer that did not, makes stale, as KWin tells no motion for it. Each is where the pointer
        // last was on the stage.
        var (left, top) = type == Gdk.EventType.MotionNotify && heard.GetPosition(out var x, out var y) ? Told(x, y) : _stage;
        _pressed = type == Gdk.EventType.ButtonPress || (_pressed && type != Gdk.EventType.ButtonRelease);
        var button = type is Gdk.EventType.ButtonPress or Gdk.EventType.ButtonRelease ? Gdk.Internal.ButtonEvent.GetButton(heard.Handle) : 0;
        var (dx, dy) = type == Gdk.EventType.Scroll ? Turned(heard.Handle) : (0, 0);
        Pointer(new ShellPointer(_id, kind, left, top, button, (uint)heard.GetModifierState(), heard.GetTime(), dx, dy));
        return false;
    }

    // A wheel's notch as a turn of one, up negative, as the extension tells it; a smooth scroll as its deltas, in notches.
    private static (double, double) Turned(nint scroll)
    {
        var direction = Gdk.Internal.ScrollEvent.GetDirection(scroll);
        if (direction == Gdk.ScrollDirection.Smooth)
        {
            Gdk.Internal.ScrollEvent.GetDeltas(scroll, out var x, out var y);
            return Gdk.Internal.ScrollEvent.GetUnit(scroll) == Gdk.ScrollUnit.Surface ? (x / PixelsPerNotch, y / PixelsPerNotch) : (x, y);
        }

        return direction == Gdk.ScrollDirection.Up ? (0, -1) : direction == Gdk.ScrollDirection.Down ? (0, 1) : (0, 0);
    }
}
