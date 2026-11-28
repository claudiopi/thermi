using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.LayerShell;

/// <summary>
/// Where a monitor's panels keep room, on each edge, as layer surfaces and their popups learn it. A layer surface is told
/// its size, never its place, and one kept off the panels' zones is given what they leave: its size tells how much room
/// they keep across and down, never on which side. A popup is told its place on its surface, moved into the area the
/// compositor keeps popups in: KWin keeps them off the panels, so one the monitor's size on a surface over the whole
/// monitor shrinks to the work area; Sway keeps them on the monitor, so one the monitor's size, ending at the corner of the
/// surface kept off the panels, slides out to the monitor's corner, as far as the room on the left and the top. Where
/// neither tells, the room is taken to be on the left and the top. Nothing of it shows or takes the pointer. It stays as
/// long as its monitor: a panel that maps after thermi, as plasmashell or a bar Sway's config starts, one that grows,
/// moves or hides itself changes the size of the surface kept off the panels, and the room is measured again. A compositor
/// that gives it its size again for each of its frames, as Hyprland, would keep it drawing: it is told restless, and goes.
/// </summary>
internal sealed class LayerProbe : IDisposable
{
    // GLib's priority of idle callbacks, past GDK's redraw: a frame drawn of a new size is sent before one runs.
    private const int AfterFrame = 200;

    // The sizes given it, past a measure told, that a compositor giving sizes only as they change never gives.
    private const int Restless = 30;

    private readonly Action<LayerProbe> _measured;
    private readonly Action<LayerProbe> _restless;
    private readonly ILogger _log;
    private readonly List<Gtk.DrawingArea> _areas = [];
    private readonly List<Gdk.Surface> _popups = [];
    private readonly Gtk.Window _keptOff;
    private readonly Gtk.Window _over;
    private (int Width, int Height)? _inside;
    private bool _overShown;
    private (int X, int Y, int Width, int Height)? _shrunk;
    private (int X, int Y)? _slid;
    private int _asking;
    private int _given = -1;
    private bool _disposed;

    // Each measure counts, so what an earlier one still waits for, as a panel changes again, tells nothing.
    private int _round;

    public LayerProbe(Gdk.Monitor monitor, Action<LayerProbe> measured, Action<LayerProbe> restless, ILogger log)
    {
        Monitor = monitor;
        (_measured, _restless, _log) = (measured, restless, log);
        _keptOff = Surface(zone: 0, size =>
        {
            _inside = size;
            Measure();
        });
        _over = Surface(zone: -1, _ =>
        {
            _overShown = true;
            Measure();
        });
    }

    public Gdk.Monitor Monitor { get; }

    /// <summary>
    /// The room the panels keep along each edge, in the monitor's logical pixels, as last measured, and whether the
    /// compositor told which edges it is on, or it was taken to be on the left and the top.
    /// </summary>
    public (PanelRoom Room, bool Told) Found => RoomOf((Monitor.Geometry.Width, Monitor.Geometry.Height), _inside ?? (0, 0), _shrunk, _slid);

    /// <summary>Measures the room again, as the monitor changed; told once measured, in place of a measure still under way.</summary>
    public void Measure()
    {
        if (_disposed || _inside is null || !_overShown)
        {
            return;
        }

        Drop();
        var round = ++_round;
        (_asking, _shrunk, _slid, _given) = (2, null, null, -1);
        Pop(_keptOff, Gdk.Gravity.SouthEast, Gdk.AnchorHints.Slide, round, told => _slid = told is var (x, y, _, _) ? (x, y) : null);
        Pop(_over, Gdk.Gravity.NorthWest, Gdk.AnchorHints.Resize, round, told => _shrunk = told);
    }

    /// <summary>
    /// The room on each edge of a monitor, from the size a surface kept off the panels is given, the popup on the whole
    /// monitor as the compositor shrunk it, and the place the popup ending at the corner of the surface kept off the panels
    /// slid to: either is null where the compositor told nothing. How much room there is only the panels' zones say: the
    /// popup shrunk tells its edges only where it keeps that room, as KWin's does, and not where the compositor keeps every
    /// popup some pixels inside its monitor, whatever the panels keep, as Hyprland does.
    /// </summary>
    public static (PanelRoom Room, bool Told) RoomOf(
        (int Width, int Height) monitor, (int Width, int Height) inside, (int X, int Y, int Width, int Height)? shrunk, (int X, int Y)? slid)
    {
        var (across, down) = (Math.Max(0, monitor.Width - inside.Width), Math.Max(0, monitor.Height - inside.Height));
        if (shrunk is var (x, y, width, height) && (across > 0 || down > 0) && (monitor.Width - width, monitor.Height - height) == (across, down)
            && x >= 0 && y >= 0)
        {
            return (new PanelRoom(x, y, Math.Max(0, monitor.Width - x - width), Math.Max(0, monitor.Height - y - height)), true);
        }

        if (slid is var (left, top) && left <= 0 && -left <= across && top <= 0 && -top <= down)
        {
            return (new PanelRoom(-left, -top, across + left, down + top), true);
        }

        return (new PanelRoom(across, down, 0, 0), across == 0 && down == 0);
    }

    // What its signals left for later, a size or a restless count, is for a probe that went no more.
    public void Dispose()
    {
        _disposed = true;
        _round++;
        Drop();
        _keptOff.Destroy();
        _over.Destroy();
        MainLoopRelease.Hold(_log, [_keptOff, _over, .. _areas]);
        _keptOff.Dispose();
        _over.Dispose();
        _areas.ForEach(area => area.Dispose());
        _areas.Clear();
    }

    private void Drop()
    {
        foreach (var popup in _popups)
        {
            // GDK's destroy drops the reference a popup's maker holds, which GirCore's wrapper gives back too as it is disposed:
            // the destroy gets one more of its own, and what GirCore's copies of the popup's signals still hold is freed on the
            // main loop (MainLoopRelease).
#pragma warning disable S3869 // GirCore has no call that refs an object but on its pointer.
            _ = GObject.Internal.Object.Ref(popup.Handle.DangerousGetHandle());
#pragma warning restore S3869
            popup.Destroy();
            MainLoopRelease.Hold(_log, popup);
            popup.Dispose();
        }

        _popups.Clear();
    }

    // A layer surface anchored to every edge, over the panels' zones or kept off them, told its size each time it changes,
    // once it shows a frame of that size, which a popup needs of its surface.
    private Gtk.Window Surface(int zone, Action<(int Width, int Height)> sized)
    {
        var window = Gtk.Window.New();
        var area = Gtk.DrawingArea.New();
        _areas.Add(area);
        LayerShellLibrary.Init(window);
        LayerShellLibrary.SetNamespace(window, "thermi-probe");
        LayerShellLibrary.SetLayer(window, Layer.Background);
        LayerShellLibrary.SetMonitor(window, Monitor);
        foreach (var edge in Enum.GetValues<Edge>())
        {
            LayerShellLibrary.SetAnchor(window, edge, anchored: true);
        }

        LayerShellLibrary.SetExclusiveZone(window, zone);
        PictureWindow.Unpainted(window);
        window.SetChild(area);
        (int Width, int Height)? last = null;
        area.OnResize += (_, size) =>
        {
            var at = (size.Width, size.Height);
            if (size.Width > 0 && size.Height > 0 && at != last)
            {
                last = at;
                GLib.Functions.IdleAdd(AfterFrame, () => Alive(() => sized(at)));
            }
        };
        window.OnRealize += (_, _) =>
        {
            PictureWindow.TakeNoPointer(window);
            if (window.GetSurface() is { } surface)
            {
                surface.OnLayout += (_, _) => Given();
            }
        };
        window.Present();
        return window;
    }

    // A popup the monitor's size from the surface's top-left corner, its own corner there, moved as the hints let the
    // compositor; told where it went and how big, or null where it never shows. It is never drawn, so it never shows.
    private void Pop(Gtk.Window window, Gdk.Gravity corner, Gdk.AnchorHints hints, int round, Action<(int X, int Y, int Width, int Height)?> told)
    {
        if (window.GetSurface() is not { } parent)
        {
            Answered(round, () => told(null));
            return;
        }

        var popup = Gdk.Surface.NewPopup(parent, autohide: false);
        _popups.Add(popup);
#pragma warning disable S3869 // GirCore wraps a popup of GDK's Wayland backend as a plain surface, so GdkPopup's calls take its pointer.
        var handle = popup.Handle.DangerousGetHandle();
#pragma warning restore S3869
        var heard = false;
        popup.OnLayout += (_, args) =>
        {
            if (!heard)
            {
                heard = true;
                var at = (Gdk.Internal.Popup.GetPositionX(handle), Gdk.Internal.Popup.GetPositionY(handle), args.Width, args.Height);
                Answered(round, () => told(at));
            }
        };
        using var anchor = new Gdk.Rectangle { X = 0, Y = 0, Width = 1, Height = 1 };
        using var layout = Gdk.PopupLayout.New(anchor, Gdk.Gravity.NorthWest, corner);
        layout.SetAnchorHints(hints);
        if (!Gdk.Internal.Popup.Present(handle, Monitor.Geometry.Width, Monitor.Geometry.Height, layout.Handle))
        {
            heard = true;
            Answered(round, () => told(null));
        }
    }

    // Told once both popups of this round were, out of the signal that told the last, which a popup disposed in it would
    // leave holding a freed one.
    private void Answered(int round, Action answer)
    {
        if (round != _round)
        {
            return;
        }

        answer();
        if (--_asking == 0)
        {
            GLib.Functions.IdleAdd(AfterFrame, () =>
            {
                if (round == _round)
                {
                    // A popup left on a surface keeps GTK from taking the next size the compositor gives it, as a panel
                    // comes or goes.
                    Drop();
                    _given = 0;
                    _measured(this);
                }

                return false;
            });
        }
    }

    // Told out of GDK's own signal, which a probe disposed in it would leave holding a freed surface.
    private void Given()
    {
        if (!_disposed && _given >= 0 && ++_given == Restless)
        {
            GLib.Functions.IdleAdd(0, () => Alive(() => _restless(this)));
        }
    }

    // What a signal left for later, done once only while the probe is: false, as an idle callback done for good says.
    private bool Alive(Action later)
    {
        if (!_disposed)
        {
            later();
        }

        return false;
    }
}
