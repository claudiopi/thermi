using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;

namespace Thermi.Linux.X11;

/// <summary>
/// The surfaces of an X11 desktop (docs/adr/0019-linux.md): each picture a frameless GTK window of its own, which the
/// window manager keeps above the others, off the taskbar and the pager, and gives no keyboard. It is a utility window,
/// not a dock: a dock would reserve room as a panel does in some window managers, and stay above a window gone full
/// screen, which covers a utility one. The panels' struts make each monitor's work area, as the window manager says it.
/// </summary>
internal sealed class X11Surfaces : IOverlaySurfaces
{
    // Thermi reads the pointer as often on Windows while an overlay may fade.
    private const uint WatchEvery = 100;

    private readonly Dictionary<uint, Shown> _shown = [];
    private uint _watching;
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
            list.OnItemsChanged += (_, _) => MonitorsChanged(Monitors());
        }

        Arrived();
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
        shown.Window.Show(picture, (0, 0));
        shown.Move();
        return Task.CompletedTask;
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
                window.OnRealize += (_, _) => XWindows.Realized(window.GetSurface()!);
                window.OnMap += (_, _) =>
                {
                    XWindows.Mapped(window.GetSurface()!);
                    _mapped = true;
                    Move();
                };
            })
            {
                Pointer = each => surfaces.Pointer(each),
                Key = (on, key) => surfaces.Key(on, key),
            };
        }

        public PictureWindow Window { get; }

        public (double X, double Y) At { get; set; }

        public void Move()
        {
            Window.Origin = At;
            if (_mapped && Window.Window.GetSurface() is { } surface)
            {
                XWindows.Move(surface, (int)Math.Round(At.X), (int)Math.Round(At.Y));
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
