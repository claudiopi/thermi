using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;
using Thermi.Linux.Overlay;

namespace Thermi.Linux.LayerShell;

/// <summary>
/// The surfaces of a Wayland compositor with layer surfaces (docs/adr/0019-linux.md): each picture a layer surface of its
/// own on the top layer, above every window and panel, anchored to its monitor's top-left corner over the panels' zones and
/// kept as far in as it goes, as big as its picture. It never takes the keyboard but for Esc while a press holds it. A
/// window gone full screen covers the top layer, as compositors stack it, so nothing here hides it then. The compositor
/// tells the pointer only on a surface, so a fade sees the pointer only on its overlay. A surface goes to another monitor
/// only mapped again, which ends the press on it, so a drag onto another monitor shows the picture on a surface of its
/// own there, while the one pressed stays, drawing nothing, until the press ends on it. Surfaces of one layer stack as
/// they were mapped, so what a press moves, the overlay or the pill it lifted, goes up to the overlay layer until the press
/// ends, above the snap marks mapped after it, as GNOME's extension raises it. Each monitor's panels are measured as it
/// comes, and again each time they change (<see cref="LayerProbe"/>). A compositor closes a surface only as its monitor
/// goes: it is taken off, and shown again where thermi places it next.
/// </summary>
internal sealed class LayerSurfaces(ILogger log) : IOverlaySurfaces
{
    private readonly Dictionary<uint, (PictureWindow Window, Gdk.Monitor Monitor)> _shown = [];
    private readonly HashSet<uint> _held = [];
    private readonly Dictionary<uint, PictureWindow> _pressed = [];
    private readonly HashSet<PictureWindow> _raised = [];
    private readonly Dictionary<Gdk.Monitor, PanelRoom> _rooms = [];
    private readonly Dictionary<Gdk.Monitor, LayerProbe> _probes = [];
    private readonly HashSet<Gdk.Monitor> _watched = [];
    private IReadOnlyList<Gdk.Monitor> _monitors = [];
    private IReadOnlyList<ShellMonitor> _told = [];
    private bool _untoldSaid;

    public bool Ready { get; private set; }

    public bool PointerAnywhere => false;

    public Action Arrived { get; set; } = () => { };

    public Action<IReadOnlyList<ShellMonitor>> MonitorsChanged { get; set; } = _ => { };

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    public Action<uint, string> Key { get; set; } = (_, _) => { };

    public Action<uint, int> Chosen { get; set; } = (_, _) => { };

    public Action<uint> Lost { get; set; } = _ => { };

    public void Start()
    {
        if (Gdk.Display.GetDefault()?.GetMonitors() is { } list)
        {
            list.OnItemsChanged += (_, _) => Probe();
        }

        Probe();
    }

    public Task<IReadOnlyList<ShellMonitor>> MonitorsAsync() => Task.FromResult<IReadOnlyList<ShellMonitor>>(Monitors());

    public Task Show(uint id, ShellPicture picture, double x, double y)
    {
        if (!Ready || _monitors.Count == 0)
        {
            return Task.CompletedTask;
        }

#pragma warning disable CA2000 // Kept in _shown, and disposed as it hides.
        var shown = _shown.TryGetValue(id, out var known) ? known : (New(id), _monitors[0]);
#pragma warning restore CA2000
        var (placed, shows) = Placed(id, shown, x, y, picture.Scale, picture);
        _shown[id] = placed;
        return shows;
    }

    public Task Move(uint id, double x, double y)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            var (placed, shows) = Placed(id, shown, x, y, shown.Monitor.GetScale(), picture: null);
            _shown[id] = placed;
            if (_held.Count > 0 && _raised.Add(placed.Window))
            {
                LayerShellLibrary.SetLayer(placed.Window.Window, Layer.Overlay);
            }

            return shows;
        }

        return Task.CompletedTask;
    }

    public Task Hide(uint id)
    {
        if (_shown.Remove(id, out var shown))
        {
            _raised.Remove(shown.Window);
            shown.Window.Dispose();
        }

        Let(id);
        return Task.CompletedTask;
    }

    public Task Fade(uint id, double opacity) => _shown.TryGetValue(id, out var shown) ? shown.Window.Fade(opacity) : Task.CompletedTask;

    public Task Menu(uint id, double x, double y, IReadOnlyList<ShellRow> rows) =>
        Menu(id, rows, new PillBox(x, y, 1, 1), MenuSide.Below);

    // The compositor gives a popup the keyboard only through a surface that takes it, which an overlay does only while its
    // menu is open.
    public Task Menu(uint id, IReadOnlyList<ShellRow> rows, PillBox anchor, MenuSide side)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            var window = shown.Window;
            LayerShellLibrary.SetKeyboard(window.Window, taken: true);
            window.Menu(
                rows,
                anchor,
                side == MenuSide.Below ? Gtk.PositionType.Bottom : Gtk.PositionType.Top,
                row => Chosen(id, row),
                () => LayerShellLibrary.SetKeyboard(window.Window, _held.Contains(id)));
        }

        return Task.CompletedTask;
    }

    public void Cursor(uint id, PointerShape shape)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            shown.Window.Cursor(shape);
        }

        // The press may have begun on a surface the picture left, which still has the pointer.
        if (_pressed.TryGetValue(id, out var pressed))
        {
            pressed.Cursor(shape);
        }
    }

    public Task Copy(byte[] png)
    {
        PictureWindow.Copy(png);
        return Task.CompletedTask;
    }

    public void HoldKeys(uint id, bool held)
    {
        if (held)
        {
            _held.Add(id);
        }
        else
        {
            Let(id);
        }

        if (_shown.TryGetValue(id, out var shown))
        {
            LayerShellLibrary.SetKeyboard(shown.Window.Window, held);
        }
    }

    public void Watch(bool on)
    {
        // The compositor tells no client where the pointer is off its surfaces.
    }

    public Task<string> ActivationAsync(uint time) => LaunchTokens.Of(time);

    public void Dispose()
    {
        _probes.Values.ToList().ForEach(probe => probe.Dispose());
        _probes.Clear();
        foreach (var (window, _) in _shown.Values)
        {
            window.Dispose();
        }

        _shown.Clear();
        foreach (var window in _pressed.Values)
        {
            window.Dispose();
        }

        _pressed.Clear();
    }

    private PictureWindow New(uint id)
    {
        var window = new PictureWindow(id, Made, leaveLoses: true, log)
        {
            Pointer = each => Pointer(each),
            Key = (on, key) => Key(on, key),
            Lost = on => Lost(on),
        };
        window.Closed = () => window.Window.SetVisible(false);
        return window;
    }

    // The press ended: the surface it began on, if the picture left it, goes once the release that ended it is handled.
    // What the press raised goes back to the top layer, under a window gone full screen.
    private void Let(uint id)
    {
        _held.Remove(id);
        if (_pressed.TryGetValue(id, out var pressed))
        {
            _pressed.Remove(id);
            _raised.Remove(pressed);
            GLib.Functions.IdleAdd(0, () =>
            {
                pressed.Dispose();
                return false;
            });
        }

        if (_held.Count == 0)
        {
            foreach (var raised in _raised)
            {
                LayerShellLibrary.SetLayer(raised.Window, Layer.Top);
            }

            _raised.Clear();
        }
    }

    private static void Made(Gtk.Window window)
    {
        LayerShellLibrary.Init(window);
        LayerShellLibrary.SetNamespace(window, "thermi");
        LayerShellLibrary.SetLayer(window, Layer.Top);
        LayerShellLibrary.SetAnchor(window, Edge.Top, anchored: true);
        LayerShellLibrary.SetAnchor(window, Edge.Left, anchored: true);
        LayerShellLibrary.SetExclusiveZone(window, -1);
        LayerShellLibrary.SetKeyboard(window, taken: false);
    }

    // On the monitor the corner is on, a whole logical pixel in, and the rest as the picture's own pixels inside the
    // surface: the compositor puts the surface's corner on the pixel its logical one rounds to. A picture that goes to a
    // surface of its own on another monitor during a press shows there as the one it leaves showed it.
    private ((PictureWindow Window, Gdk.Monitor Monitor) Placed, Task Shows) Placed(
        uint id, (PictureWindow Window, Gdk.Monitor Monitor) shown, double x, double y, double scale, ShellPicture? picture)
    {
        var monitor = _monitors.FirstOrDefault(each => each.Geometry.ContainsPoint((int)Math.Floor(x), (int)Math.Floor(y))) ?? shown.Monitor;
        var place = monitor.Geometry;
        var (left, top) = (PictureWindow.OnPixel(x - place.X, scale), PictureWindow.OnPixel(y - place.Y, scale));
        int Rest(double at, int whole)
        {
            return Math.Max(0, (int)Math.Round(at * scale) - (int)Math.Round(whole * scale, MidpointRounding.AwayFromZero));
        }

        var offset = (Rest(x - place.X, left), Rest(y - place.Y, top));
        PictureWindow? behind = null;
        if (monitor != shown.Monitor && _held.Contains(id) && !_pressed.ContainsKey(id) && shown.Window.Window.GetVisible())
        {
            behind = shown.Window;
            _pressed[id] = behind;
            shown = (New(id), monitor);
            _ = shown.Window.Fade(behind.Opacity);
        }

        var window = shown.Window.Window;
        if (monitor != shown.Monitor || !window.GetVisible())
        {
            LayerShellLibrary.SetMonitor(window, monitor);
        }

        LayerShellLibrary.SetMargin(window, Edge.Left, left);
        LayerShellLibrary.SetMargin(window, Edge.Top, top);
        var shows = picture is not null ? shown.Window.Show(picture, offset)
            : behind is not null ? shown.Window.ShowLike(behind, offset)
            : shown.Window.Place(offset);
        behind?.Blank();
        shown.Window.MoveTo((place.X + left, place.Y + top));
        return ((shown.Window, monitor), shows);
    }

    // A monitor that comes is measured, and one that goes takes its measure with it; the overlays show once every monitor
    // is. A monitor whose place or scale changes is measured again.
    private void Probe()
    {
        _monitors = GdkMonitors.All();
        foreach (var gone in _probes.Keys.Union(_rooms.Keys).Except(_monitors).ToList())
        {
            _probes.Remove(gone, out var probe);
            probe?.Dispose();
            _rooms.Remove(gone);
        }

        foreach (var monitor in _monitors.Where(each => !_probes.ContainsKey(each) && !_rooms.ContainsKey(each)))
        {
            if (_watched.Add(monitor))
            {
                monitor.OnNotify += (_, _) => Measure(monitor);
            }

            _probes[monitor] = new LayerProbe(monitor, Measured, Restless, log);
        }

        Ready = Ready && _monitors.Count > 0;
        Tell();
    }

    private void Measured(LayerProbe probe)
    {
        var (room, told) = probe.Found;
        _rooms[probe.Monitor] = room;
        if (!told && !_untoldSaid)
        {
            log.PanelEdgesUntold();
            _untoldSaid = true;
        }

        Tell();
    }

    // A monitor whose restless probe went is measured by a new one, which goes as the last did.
    private void Measure(Gdk.Monitor monitor)
    {
        if (_probes.TryGetValue(monitor, out var probe))
        {
            probe.Measure();
        }
        else if (_monitors.Contains(monitor))
        {
            _probes[monitor] = new LayerProbe(monitor, Measured, Restless, log);
        }
    }

    // The room it measured stays, as measured again only as its monitor changes.
    private void Restless(LayerProbe probe)
    {
        if (_probes.GetValueOrDefault(probe.Monitor) == probe)
        {
            _probes.Remove(probe.Monitor);
            probe.Dispose();
        }
    }

    // Once every monitor is measured: the first time, the surfaces arrived; after, the monitors changed, if they did.
    private void Tell()
    {
        if (_monitors.Count == 0 || !_monitors.All(_rooms.ContainsKey))
        {
            return;
        }

        var now = Monitors();
        if (!Ready)
        {
            (Ready, _told) = (true, now);
            Arrived();
        }
        else if (!now.SequenceEqual(_told))
        {
            _told = now;
            Raise();
            MonitorsChanged(now);
        }
    }

    // A panel that maps after the overlays stacks above them, as a compositor stacks the later of two surfaces of a layer;
    // shown again, each maps after it and stacks above it, as Windows keeps a topmost window above a taskbar that starts
    // later. One held keeps its press, and stays.
    private void Raise()
    {
        foreach (var (id, (window, _)) in _shown)
        {
            if (!_held.Contains(id) && window.Window.GetVisible())
            {
                window.Window.SetVisible(false);
                window.Window.Present();
            }
        }
    }

    public IReadOnlyList<ShellMonitor> Monitors() =>
        [.. GdkMonitors.Read(_monitors, monitor => _rooms.GetValueOrDefault(monitor), primary: null)];
}
