using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Thermi.Linux.X11;

/// <summary>
/// What the window manager says on the root window as it changes, which GTK 4 tells no one: the panels' room, as the work
/// area or the clients change; the stacking of the windows; and the window the person works in, or its state, as it goes
/// full screen or comes back. It watches the active window's own properties as well, as its state is its own, and a
/// connection of its own (<see cref="XcbConnection"/>) so GDK sees none of these events.
/// </summary>
internal sealed class RootWatch : IDisposable
{
    private const byte PropertyNotify = 28;
    private const uint PropertyChange = 0x400000;

    private readonly XcbConnection _x;
    private readonly Func<uint> _active;
    private readonly uint _activeAtom;
    private readonly uint _state;
    private readonly uint _stacking;
    private readonly HashSet<uint> _panels;
    private uint _watched;

    /// <summary>The root window, and <paramref name="active"/>, the window the person works in as GDK's connection reads it.</summary>
    public RootWatch(ILogger log, Func<uint> active)
    {
        _active = active;
        _x = new XcbConnection("the X11 root window's change", log) { Event = Heard };
        (_activeAtom, _state, _stacking) = (_x.Atom("_NET_ACTIVE_WINDOW"), _x.Atom("_NET_WM_STATE"), _x.Atom("_NET_CLIENT_LIST_STACKING"));
        _panels = [_x.Atom("_NET_WORKAREA"), _x.Atom("_NET_CLIENT_LIST")];
        _x.Listen(_x.Root, PropertyChange);
        WatchActive();
        _x.Flush();
    }

    /// <summary>The panels' room may have changed.</summary>
    public Action Panels { get; set; } = () => { };

    /// <summary>The windows stack in a new order.</summary>
    public Action Stacked { get; set; } = () => { };

    /// <summary>Another window is the active one, or the active one's state changed.</summary>
    public Action Active { get; set; } = () => { };

    public void Dispose() => _x.Dispose();

    private void Heard(nint each)
    {
        if ((Marshal.ReadByte(each) & 0x7F) != PropertyNotify)
        {
            return;
        }

        // The window and the property, at their places in xcb_property_notify_event_t.
        var (window, atom) = ((uint)Marshal.ReadInt32(each, 4), (uint)Marshal.ReadInt32(each, 8));
        if (window == _x.Root && atom == _activeAtom)
        {
            WatchActive();
            Active();
        }
        else if (window != _x.Root && window == _watched && atom == _state)
        {
            Active();
        }
        else if (window == _x.Root && atom == _stacking)
        {
            Stacked();
        }
        else if (window == _x.Root && _panels.Contains(atom))
        {
            Panels();
        }
    }

    // The window watched before may be gone, which the server answers with an error event, which no one reads.
    private void WatchActive()
    {
        var active = _active();
        if (active == _watched)
        {
            return;
        }

        if (_watched != 0)
        {
            _x.Listen(_watched, 0);
        }

        _watched = active;
        if (active != 0)
        {
            _x.Listen(active, PropertyChange);
        }

        _x.Flush();
    }
}
