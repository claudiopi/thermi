using Microsoft.Extensions.Logging;
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
/// ends, above the snap marks mapped after it, as GNOME's extension raises it.
/// </summary>
internal sealed class LayerSurfaces(ILogger log) : IOverlaySurfaces
{
    private readonly Dictionary<uint, (PictureWindow Window, Gdk.Monitor Monitor)> _shown = [];
    private readonly Dictionary<uint, ShellPicture> _pictures = [];
    private readonly HashSet<uint> _held = [];
    private readonly Dictionary<uint, PictureWindow> _pressed = [];
    private readonly HashSet<PictureWindow> _raised = [];
    private readonly Dictionary<Gdk.Monitor, PanelRoom> _rooms = [];
    private readonly List<LayerProbe> _probes = [];
    private readonly HashSet<Gdk.Monitor> _watched = [];
    private IReadOnlyList<Gdk.Monitor> _monitors = [];
    private bool _untoldSaid;

    public bool Ready { get; private set; }

    public bool PointerAnywhere => false;

    public Action Arrived { get; set; } = () => { };

    public Action<IReadOnlyList<ShellMonitor>> MonitorsChanged { get; set; } = _ => { };

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    public Action<uint, string> Key { get; set; } = (_, _) => { };

    public Action<uint, int> Chosen { get; set; } = (_, _) => { };

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

        _pictures[id] = picture;
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

        _pictures.Remove(id);
        Let(id);
        return Task.CompletedTask;
    }

    public Task Fade(uint id, double opacity) => _shown.TryGetValue(id, out var shown) ? shown.Window.Fade(opacity) : Task.CompletedTask;

    // The compositor gives a popup the keyboard only through a surface that takes it, which an overlay does only while its
    // menu is open.
    public Task Menu(uint id, double x, double y, IReadOnlyList<ShellRow> rows)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            var window = shown.Window;
            LayerShellLibrary.SetKeyboard(window.Window, exclusive: true);
            window.Menu(rows, x, y, row => Chosen(id, row), () => LayerShellLibrary.SetKeyboard(window.Window, _held.Contains(id)));
        }

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

    public void Dispose()
    {
        _probes.ForEach(probe => probe.Dispose());
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

    private PictureWindow New(uint id) => new(id, Made) { Pointer = each => Pointer(each), Key = (on, key) => Key(on, key) };

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
        LayerShellLibrary.SetKeyboard(window, exclusive: false);
    }

    // On the monitor the corner is on, a whole logical pixel in, and the rest as the picture's own pixels inside the
    // surface: the compositor puts the surface's corner on the pixel its logical one rounds to.
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
        if (monitor != shown.Monitor && _held.Contains(id) && !_pressed.ContainsKey(id) && shown.Window.Window.GetVisible())
        {
            var opacity = shown.Window.Opacity;
            shown.Window.Blank();
            _pressed[id] = shown.Window;
            shown = (New(id), monitor);
            _ = shown.Window.Fade(opacity);
            picture ??= _pictures.GetValueOrDefault(id);
        }

        var window = shown.Window.Window;
        if (monitor != shown.Monitor || !window.GetVisible())
        {
            LayerShellLibrary.SetMonitor(window, monitor);
        }

        LayerShellLibrary.SetMargin(window, Edge.Left, left);
        LayerShellLibrary.SetMargin(window, Edge.Top, top);
        shown.Window.Origin = (place.X + left, place.Y + top);
        var shows = picture is not null ? shown.Window.Show(picture, offset) : shown.Window.Place(offset);
        return ((shown.Window, monitor), shows);
    }

    // Each monitor measured again, as one came or went: the overlays show once every monitor is.
    private void Probe()
    {
        _probes.ForEach(probe => probe.Dispose());
        _probes.Clear();
        _rooms.Clear();
        _monitors = GdkMonitors.All();
        Ready = Ready && _monitors.Count > 0;
        foreach (var monitor in _monitors)
        {
            if (_watched.Add(monitor))
            {
                monitor.OnNotify += (_, _) => Probe();
            }

            _probes.Add(new LayerProbe(monitor, Measured));
        }
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

        _probes.Remove(probe);
        probe.Dispose();
        if (_probes.Count > 0)
        {
            return;
        }

        var first = !Ready;
        Ready = _monitors.Count > 0;
        if (first && Ready)
        {
            Arrived();
        }
        else
        {
            MonitorsChanged(Monitors());
        }
    }

    public IReadOnlyList<ShellMonitor> Monitors() =>
        [.. GdkMonitors.Read(_monitors, monitor => _rooms.GetValueOrDefault(monitor), primary: null)];
}
