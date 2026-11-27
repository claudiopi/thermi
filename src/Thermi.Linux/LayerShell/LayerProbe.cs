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
/// neither tells, the room is taken to be on the left and the top. Nothing of it shows or takes the pointer, and it goes
/// once measured.
/// </summary>
internal sealed class LayerProbe : IDisposable
{
    // GLib's priority of idle callbacks, past GDK's redraw: a frame drawn of a new size is sent before one runs.
    private const int AfterFrame = 200;

    private readonly Action<LayerProbe> _measured;
    private readonly List<IDisposable> _owned = [];
    private readonly List<Gdk.Surface> _popups = [];
    private (int Width, int Height)? _inside;
    private (int X, int Y, int Width, int Height)? _shrunk;
    private (int X, int Y)? _slid;
    private int _asking = 2;

    public LayerProbe(Gdk.Monitor monitor, Action<LayerProbe> measured)
    {
        Monitor = monitor;
        _measured = measured;
        Surface(zone: 0, (window, size) =>
        {
            _inside = size;
            Pop(window, Gdk.Gravity.SouthEast, Gdk.AnchorHints.Slide, told => _slid = told is var (x, y, _, _) ? (x, y) : null);
        });
        Surface(zone: -1, (window, _) => Pop(window, Gdk.Gravity.NorthWest, Gdk.AnchorHints.Resize, told => _shrunk = told));
    }

    public Gdk.Monitor Monitor { get; }

    /// <summary>
    /// The room the panels keep along each edge, in the monitor's logical pixels, once measured, and whether the compositor
    /// told which edges it is on, or it was taken to be on the left and the top.
    /// </summary>
    public (PanelRoom Room, bool Told) Found => RoomOf((Monitor.Geometry.Width, Monitor.Geometry.Height), _inside ?? (0, 0), _shrunk, _slid);

    // The room on each edge of a monitor, from the size a surface kept off the panels is given, the popup on the whole
    // monitor as the compositor shrunk it, and the place the popup ending at the corner of the surface kept off the panels
    // slid to: either is null where the compositor told nothing.
    private static (PanelRoom Room, bool Told) RoomOf(
        (int Width, int Height) monitor, (int Width, int Height) inside, (int X, int Y, int Width, int Height)? shrunk, (int X, int Y)? slid)
    {
        var (across, down) = (Math.Max(0, monitor.Width - inside.Width), Math.Max(0, monitor.Height - inside.Height));
        if (shrunk is var (x, y, width, height) && (width < monitor.Width || height < monitor.Height) && x >= 0 && y >= 0)
        {
            return (new PanelRoom(x, y, Math.Max(0, monitor.Width - x - width), Math.Max(0, monitor.Height - y - height)), true);
        }

        if (slid is var (left, top) && left <= 0 && -left <= across && top <= 0 && -top <= down)
        {
            return (new PanelRoom(-left, -top, across + left, down + top), true);
        }

        return (new PanelRoom(across, down, 0, 0), across == 0 && down == 0);
    }

    public void Dispose()
    {
        foreach (var popup in _popups)
        {
            // GDK's destroy drops a reference of its own, besides the one GirCore's wrapper gives back as it is disposed: without
            // one more for it, a copy GirCore keeps of a signal's arguments is left holding a freed popup, which .NET's finalizer
            // then unrefs, and thermi crashes.
#pragma warning disable S3869 // GirCore has no call that refs an object but on its pointer.
            _ = GObject.Internal.Object.Ref(popup.Handle.DangerousGetHandle());
#pragma warning restore S3869
            popup.Destroy();
            popup.Dispose();
        }

        _popups.Clear();
        foreach (var each in _owned)
        {
            if (each is Gtk.Window window)
            {
                window.Destroy();
            }

            each.Dispose();
        }

        _owned.Clear();
    }

    // A layer surface anchored to every edge, over the panels' zones or kept off them, told its size once, and once it shows
    // a frame, which a popup needs of its surface, given to what asks a popup of it.
    private void Surface(int zone, Action<Gtk.Window, (int Width, int Height)> sized)
    {
        var window = Gtk.Window.New();
        var area = Gtk.DrawingArea.New();
        _owned.Add(window);
        _owned.Add(area);
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
        var asked = false;
        area.OnResize += (_, size) =>
        {
            if (size.Width > 0 && size.Height > 0 && !asked)
            {
                asked = true;
                var at = (size.Width, size.Height);
                GLib.Functions.IdleAdd(AfterFrame, () =>
                {
                    sized(window, at);
                    return false;
                });
            }
        };
        window.OnRealize += (_, _) => PictureWindow.TakeNoPointer(window);
        window.Present();
    }

    // A popup the monitor's size from the surface's top-left corner, its own corner there, moved as the hints let the
    // compositor; told where it went and how big, or null where it never shows. It is never drawn, so it never shows.
    private void Pop(Gtk.Window window, Gdk.Gravity corner, Gdk.AnchorHints hints, Action<(int X, int Y, int Width, int Height)?> told)
    {
        if (window.GetSurface() is not { } parent)
        {
            Answered(() => told(null));
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
                Answered(() => told(at));
            }
        };
        using var anchor = new Gdk.Rectangle { X = 0, Y = 0, Width = 1, Height = 1 };
        using var layout = Gdk.PopupLayout.New(anchor, Gdk.Gravity.NorthWest, corner);
        layout.SetAnchorHints(hints);
        if (!Gdk.Internal.Popup.Present(handle, Monitor.Geometry.Width, Monitor.Geometry.Height, layout.Handle))
        {
            heard = true;
            Answered(() => told(null));
        }
    }

    // Told once both popups were, out of the signal that told the last, as what is told disposes the popups.
    private void Answered(Action answer)
    {
        answer();
        if (--_asking == 0)
        {
            GLib.Functions.IdleAdd(AfterFrame, () =>
            {
                _measured(this);
                return false;
            });
        }
    }
}
