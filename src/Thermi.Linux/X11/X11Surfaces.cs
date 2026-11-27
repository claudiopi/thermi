using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;

namespace Thermi.Linux.X11;

/// <summary>
/// The surfaces of an X11 desktop (docs/adr/0019-linux.md): each picture a frameless GTK window of its own, which the
/// window manager keeps above the others, off the taskbar and the pager, and gives no keyboard. It is a dock, as a panel
/// is, not a utility window: window managers keep a utility window out of the panels' room, so an overlay could not sit on
/// a panel, while a dock with no strut of its own reserves nothing. Nor is it kept above, which would hold it above a
/// window gone full screen too, while a dock stays above every other window and under that one. The panels' struts make
/// each monitor's work area, as the window manager says it.
/// </summary>
internal sealed class X11Surfaces : IOverlaySurfaces
{
    // Thermi reads the pointer as often on Windows while an overlay may fade.
    private const uint WatchEvery = 100;

    private readonly Dictionary<uint, Shown> _shown = [];
    private IReadOnlyList<ShellMonitor> _known = [];
    private uint _watching;
    private uint _checking;
    private (int X, int Y)? _pointer;

    public bool Ready => true;

    public bool PointerAnywhere => true;

    public Action Arrived { get; set; } = () => { };

    public Action<IReadOnlyList<ShellMonitor>> MonitorsChanged { get; set; } = _ => { };

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    public Action<uint, string> Key { get; set; } = (_, _) => { };

    public void Start()
    {
        if (Gdk.Display.GetDefault()?.GetMonitors() is { } list)
        {
            list.OnItemsChanged += (_, _) => Changed();
        }

        // A panel that comes, goes or grows changes the root window's work area, which GTK 4 tells no one: it is read again
        // each second, a property's read.
        _checking = GLib.Functions.TimeoutAddSeconds(0, 1, () =>
        {
            if (!Monitors().SequenceEqual(_known))
            {
                Changed();
            }

            return true;
        });
        _known = Monitors();
        Arrived();
    }

    private void Changed()
    {
        _known = Monitors();
        MonitorsChanged(_known);
    }

    public Task<IReadOnlyList<ShellMonitor>> MonitorsAsync() => Task.FromResult<IReadOnlyList<ShellMonitor>>(Monitors());

    public Task Show(uint id, ShellPicture picture, double x, double y)
    {
        if (!_shown.TryGetValue(id, out var shown))
        {
            shown = new Shown(id, this);
            _shown[id] = shown;
        }

        shown.At = (x, y);
        var shows = shown.Window.Show(picture, (0, 0));
        shown.Move();
        return shows;
    }

    public Task Move(uint id, double x, double y)
    {
        if (_shown.TryGetValue(id, out var shown))
        {
            shown.At = (x, y);
            shown.Move();
        }

        return Task.CompletedTask;
    }

    public Task Hide(uint id)
    {
        if (_shown.Remove(id, out var shown))
        {
            shown.Window.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task Fade(uint id, double opacity) => _shown.TryGetValue(id, out var shown) ? shown.Window.Fade(opacity) : Task.CompletedTask;

    public void HoldKeys(uint id, bool held)
    {
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

    public void Dispose()
    {
        Watch(false);
        if (_checking != 0)
        {
            _ = GLib.Functions.SourceRemove(_checking);
        }

        foreach (var shown in _shown.Values)
        {
            shown.Window.Dispose();
        }

        _shown.Clear();
    }

    public IReadOnlyList<ShellMonitor> Monitors()
    {
        var monitors = GdkMonitors.All();
        var primary = Gdk.Display.GetDefault() is { } display ? XWindows.Primary(display) : null;
        return [.. GdkMonitors.Read(monitors, monitor => Math.Max(0, XWindows.WorkArea(monitor).Y - monitor.Geometry.Y), primary)];
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

        public Shown(uint id, X11Surfaces surfaces)
        {
            Window = new PictureWindow(id, window =>
            {
                window.OnRealize += (_, _) =>
                {
                    XWindows.Realized(window.GetSurface()!);
                    Shape();
                };
                window.OnMap += (_, _) =>
                {
                    _mapped = true;
                    Move();
                };
            })
            {
                Pointer = each => surfaces.Pointer(Rooted(each)),
                Key = (on, key) => surfaces.Key(on, key),
            };
            Window.Shaped = Shape;
        }

        public PictureWindow Window { get; }

        // The window moves only once the window manager moves it, after thermi asked, while GDK tells a point from the
        // window's corner as the X server had it: a drag would count its move twice, so past a press, which no move is
        // behind, the point is the pointer's on the root window as it is read.
        private static ShellPointer Rooted(ShellPointer pointer) =>
            pointer.Kind != "press" && Gdk.Display.GetDefault() is { } display && XWindows.Pointer(display) is var (x, y, _)
                ? pointer with { X = x, Y = y }
                : pointer;

        public (double X, double Y) At { get; set; }

        public void Move()
        {
            Window.Origin = At;
            if (_mapped && Window.Window.GetSurface() is { } surface)
            {
                XWindows.Move(surface, (int)Math.Round(At.X), (int)Math.Round(At.Y));
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
