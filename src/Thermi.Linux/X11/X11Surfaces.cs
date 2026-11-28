using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;

namespace Thermi.Linux.X11;

/// <summary>
/// The surfaces of an X11 desktop (docs/adr/0019-linux.md): each picture a frameless GTK window of its own, which the
/// window manager keeps above the others, off the taskbar and the pager, and gives no keyboard. It is a dock, as a panel
/// is, not a utility window: window managers keep a utility window out of the panels' room, so an overlay could not sit on
/// a panel, while a dock with no strut of its own reserves nothing. Nor is it kept above, which would hold it above a
/// window gone full screen too, while a dock stays above every other window and under that one; a panel that rises above
/// it, as a dock that maps after it or one the person clicks, is put back under it. The panels' struts make each monitor's
/// work area, as the window manager says it, and a monitor the active window covers is full screen, one of thermi's own
/// never counting as active, read as the root window tells each changes (<see cref="RootWatch"/>). The window manager's
/// close, as a pager's or a task switcher's, quits thermi, as Windows' overlay quits on its close.
/// </summary>
internal sealed class X11Surfaces(ILogger log) : IOverlaySurfaces
{
    // Thermi reads the pointer as often on Windows while an overlay may fade.
    private const uint WatchEvery = 100;

    private readonly Dictionary<uint, Shown> _shown = [];
    private IReadOnlyList<ShellMonitor> _known = [];
    private RootWatch? _root;
    private uint _watching;
    private (double X, double Y)? _pointer;
    private uint? _held;

    public bool Ready => true;

    public bool PointerAnywhere => true;

    public Action Arrived { get; set; } = () => { };

    public Action<IReadOnlyList<ShellMonitor>> MonitorsChanged { get; set; } = _ => { };

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    public Action<uint, string> Key { get; set; } = (_, _) => { };

    public Action<uint, int> Chosen { get; set; } = (_, _) => { };

    public Action<uint> Lost { get; set; } = _ => { };

    public void Start()
    {
        if (Gdk.Display.GetDefault() is { } display)
        {
            display.GetMonitors().OnItemsChanged += (_, _) => Changed();
            _root = new RootWatch(log, () => XWindows.Front(display, Ours()))
            {
                Panels = Changed,
                Active = Changed,
                Stacked = () => Restack(display),
            };
        }

        _known = Monitors();
        Arrived();
    }

    private void Changed()
    {
        var now = Monitors();
        if (!now.SequenceEqual(_known))
        {
            _known = now;
            MonitorsChanged(now);
        }
    }

    private void Restack(Gdk.Display display)
    {
        var ours = Ours();
        if (ours.Count > 0 && XWindows.Covered(display, ours))
        {
            XWindows.Raise(display, ours);
        }
    }

    private List<uint> Ours() => [.. _shown.Values.Select(shown => shown.Id).OfType<uint>()];

    public Task<IReadOnlyList<ShellMonitor>> MonitorsAsync() => Task.FromResult<IReadOnlyList<ShellMonitor>>(Monitors());

    public Task Show(uint id, ShellPicture picture, double x, double y)
    {
        if (!_shown.TryGetValue(id, out var shown))
        {
            shown = new Shown(id, this, log);
            _shown[id] = shown;
        }

        shown.At = (x, y);
        var shows = shown.Window.Show(picture, (0, 0));
        shown.Move(raise: false);
        return shows;
    }

    // A picture moved rises above thermi's others, as one dragged above the snap marks shown before it.
    public Task Move(uint id, double x, double y)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            shown.At = (x, y);
            shown.Move(raise: true);
        }

        return Task.CompletedTask;
    }

    // The keyboard a press took goes back before its window goes, which GTK would give to no one.
    public Task Hide(uint id)
    {
        if (_shown.Remove(id, out var shown))
        {
            shown.Hold(held: false);
            shown.Window.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task Fade(uint id, double opacity) => _shown.TryGetValue(id, out var shown) ? shown.Window.Fade(opacity) : Task.CompletedTask;

    // GTK grabs the keyboard for its menu itself, as the window takes none on a click, and chooses the side again, knowing
    // the menu's height; on X11 GTK itself keeps a menu in its monitor's work area, so no room is given.
    public Task Menu(uint id, IReadOnlyList<ShellRow> rows, PillBox anchor, MenuSide side)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            shown.Window.Menu(rows, anchor, default, row => Chosen(id, row), () => Chosen(id, -1));
        }

        return Task.CompletedTask;
    }

    public void Cursor(uint id, PointerShape shape)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            shown.Window.Cursor(shape);
        }
    }

    public Task Copy(byte[] png)
    {
        PictureWindow.Copy(png);
        return Task.CompletedTask;
    }

    public void HoldKeys(uint id, bool held)
    {
        _held = held ? id : null;
        if (_shown.TryGetValue(id, out var shown))
        {
            shown.Hold(held);
        }
    }

    public void Watch(bool on)
    {
        if (on && _watching == 0)
        {
            _watching = GLib.Functions.TimeoutAdd(0, WatchEvery, Read);
        }
        else if (!on && _watching != 0)
        {
            _ = GLib.Functions.SourceRemove(_watching);
            (_watching, _pointer) = (0, null);
        }
    }

    public Task<string> ActivationAsync(uint time) => LaunchTokens.Of(time);

    public void Dispose()
    {
        Watch(false);
        _root?.Dispose();
        foreach (var shown in _shown.Values)
        {
            shown.Window.Dispose();
        }

        _shown.Clear();
    }

    public IReadOnlyList<ShellMonitor> Monitors() =>
        Gdk.Display.GetDefault() is { } display ? MonitorsOf(log, display, XWindows.FullScreen(display, Ours())) : [];

    /// <summary>
    /// The monitors of an X11 desktop, as GDK, RandR and the panels' struts tell them, the one under the middle of
    /// <paramref name="covered"/>, a full-screen window's box, full screen: the settings window's map reads them so too, so
    /// a spot picked on it names the overlay's monitor.
    /// </summary>
    public static IReadOnlyList<ShellMonitor> MonitorsOf(ILogger log, Gdk.Display display, (double X, double Y, double Width, double Height)? covered)
    {
        bool FullScreen(Gdk.Monitor monitor)
        {
            return covered is var (x, y, width, height) && monitor.Geometry.ContainsPoint((int)(x + (width / 2)), (int)(y + (height / 2)));
        }

        return [.. GdkMonitors.Read(log, GdkMonitors.All(), PanelStruts.Of(display), XWindows.Primary(display), FullScreen, XWindows.Edid)];
    }

    // The pointer anywhere, told only when it moved, as a Pointer on no overlay.
    private bool Read()
    {
        if (Gdk.Display.GetDefault() is { } display && XWindows.Pointer(display) is var (x, y, keys) && _pointer != (x, y))
        {
            _pointer = (x, y);
            Pointer(new ShellPointer(uint.MaxValue, "near", x, y, 0, keys, 0, 0, 0));
        }

        return true;
    }

    // A window and where its corner goes, which it is moved to once it maps, and after each move.
    private sealed class Shown
    {
        private bool _mapped;
        private nuint? _focusedBefore;

        public Shown(uint id, X11Surfaces surfaces, ILogger log)
        {
            Window = new PictureWindow(id, window =>
            {
                window.OnRealize += (_, _) =>
                {
                    var surface = window.GetSurface()!;
                    XWindows.Realized(surface);
                    Shape();
                    // A window manager's frame takes the pointer where the window's shape left it when it framed or
                    // resized the window, as xfwm4's does, and looks again only once the shape changes: shaped again at
                    // each size, the frame lets through what the window does.
                    surface.OnLayout += (_, _) => Shape();
                };
                window.OnMap += (_, _) =>
                {
                    _mapped = true;
                    Move(raise: false);
                };
            }, leaveLoses: false, log)
            {
                Pointer = each => surfaces.Pointer(Rooted(each)),
                // GTK gives a window it shows the keyboard, as the lifted pill's while a press holds an overlay's: Esc on
                // any of thermi's windows is the held one's.
                Key = (on, key) => surfaces.Key(surfaces._held ?? on, key),
                Lost = on => surfaces.Lost(on),
                Closed = Quit,
            };
            Window.Shaped = Shape;
        }

        public PictureWindow Window { get; }

        // The overlay's quit, over the session bus, as the settings window asks it.
        private static void Quit()
        {
            var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
            Gio.DBusActionGroup.Get(bus, ShellLink.Name, ShellLink.ObjectPath).ActivateAction("quit", null);
        }

        public uint? Id => _mapped && Window.Window.GetSurface() is { } surface ? XWindows.Id(surface) : null;

        // The window moves only once the window manager moves it, after thermi asked, while GDK tells a point from the
        // window's corner as the X server had it: a drag would count its move twice, so past a press, which no move is
        // behind, the point is the pointer's on the root window as it is read.
        private static ShellPointer Rooted(ShellPointer pointer) =>
            pointer.Kind != "press" && Gdk.Display.GetDefault() is { } display && XWindows.Pointer(display) is var (x, y, _)
                ? pointer with { X = x, Y = y }
                : pointer;

        public (double X, double Y) At { get; set; }

        public void Move(bool raise)
        {
            Window.Origin = At;
            if (_mapped && Window.Window.GetSurface() is { } surface)
            {
                XWindows.Move(surface, At.X, At.Y, raise);
            }
        }

        // GDK 4 sets no input shape of an X11 window from its input region: the shape is set here, from the same boxes.
        private void Shape()
        {
            if (Window.Window.GetSurface() is { } surface)
            {
                XWindows.Shape(surface, Window.Clicked());
            }
        }

        public void Hold(bool held)
        {
            if (Window.Window.GetSurface() is not { } surface)
            {
                return;
            }

            if (held && _focusedBefore is null)
            {
                _focusedBefore = XWindows.Focus(surface);
            }
            else if (!held && _focusedBefore is { } before)
            {
                XWindows.Unfocus(surface, before);
                _focusedBefore = null;
            }
        }
    }
}
