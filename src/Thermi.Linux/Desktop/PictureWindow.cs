using Thermi.Linux.Gnome;

namespace Thermi.Linux.Desktop;

/// <summary>
/// A GTK window that shows one of thermi's pictures pixel for pixel, as a layer surface or an X11 window makes it: no
/// frame, nothing drawn where the picture draws nothing, and the pointer taken only on the picture's boxes. It tells the
/// pointer and Esc on it in the stage's logical pixels, from where its surface's corner is on the stage.
/// </summary>
internal sealed class PictureWindow : IDisposable
{
    private const string Style = "thermi-overlay";
    private const uint Escape = 0xff1b;

    // A scale of eighths, as Wayland's fractional scales are 120ths that compositors keep to quarters or eighths, falls
    // on a whole pixel within 8 logical ones.
    private const int Steps = 8;

    // The pointer's events as the extension names them.
    private static readonly Dictionary<Gdk.EventType, string> Kinds = new()
    {
        [Gdk.EventType.ButtonPress] = "press",
        [Gdk.EventType.ButtonRelease] = "release",
        [Gdk.EventType.MotionNotify] = "motion",
        [Gdk.EventType.Scroll] = "scroll",
    };

    private static bool _styled;

    private readonly uint _id;
    private readonly Gtk.DrawingArea _area = Gtk.DrawingArea.New();
    private readonly List<TaskCompletionSource> _waiting = [];
    private bool _drawn;
    private (double X, double Y) _heardAt;
    private Cairo.ImageSurface? _image;
    private (int X, int Y) _offset;
    private double _scale = 1;
    private IReadOnlyList<Core.Look.PillBox> _clicks = [];

    public PictureWindow(uint id, Action<Gtk.Window> made)
    {
        _id = id;
        Unpainted(Window);
        Window.SetDecorated(false);
        Window.SetResizable(false);
        Window.SetTitle("thermi");
        _area.SetDrawFunc(Draw);
        Window.SetChild(_area);
        var events = Gtk.EventControllerLegacy.New();
        // GirCore reads no GdkEvent from a signal's arguments, only from the controller.
        events.OnEvent += (controller, _) => controller.GetCurrentEvent() is { } heard && Heard(heard);
        Window.AddController(events);
        // GTK tells a pointer's coming and going only to a motion controller: where it came is its first point on the window.
        var crossing = Gtk.EventControllerMotion.New();
        crossing.OnEnter += (controller, args) => Crossed(controller, "motion", (args.X, args.Y));
        crossing.OnLeave += (controller, _) => Crossed(controller, "leave", _heardAt);
        Window.AddController(crossing);
        Window.OnRealize += (_, _) => Region();
        made(Window);
    }

    /// <summary>Where the window's corner is on the stage, in logical pixels, from which its pointer is told.</summary>
    public (double X, double Y) Origin { get; set; }

    public Gtk.Window Window { get; } = Gtk.Window.New();

    public Action<ShellPointer> Pointer { get; set; } = _ => { };

    public Action<uint, string> Key { get; set; } = (_, _) => { };

    /// <summary>
    /// Shows <paramref name="picture"/> <paramref name="offset"/> pixels in from the window's corner, so it lands on a
    /// pixel of its own where the window's corner can only be on a logical one; done once the compositor showed it.
    /// </summary>
    public Task Show(ShellPicture picture, (int X, int Y) offset)
    {
        _image?.Dispose();
        _image = new Cairo.ImageSurface(Cairo.Format.Argb32, picture.Width, picture.Height);
        var pixels = picture.Pixels.GetRegionSpan<byte>(0, picture.Pixels.GetSize());
        var into = _image.GetData();
        for (var row = 0; row < picture.Height; row++)
        {
            pixels.Slice(row * picture.Stride, picture.Width * 4).CopyTo(into[(row * _image.Stride)..]);
        }

        _image.MarkDirty();
        (_scale, _clicks) = (picture.Scale, picture.Clicks);
        var shown = Place(offset);
        if (!Window.GetVisible())
        {
            Window.Present();
        }

        return shown;
    }

    /// <summary>The picture shown <paramref name="offset"/> pixels in from the window's corner, as it moves; done once it shows.</summary>
    public Task Place((int X, int Y) offset)
    {
        if (_image is null)
        {
            return Task.CompletedTask;
        }

        _offset = offset;
        _area.SetContentWidth(Covering(_image.Width + offset.X, _scale));
        _area.SetContentHeight(Covering(_image.Height + offset.Y, _scale));
        Region();
        _area.QueueDraw();
        var done = new TaskCompletionSource();
        _waiting.Add(done);
        _drawn = false;
        if (_waiting.Count == 1)
        {
            _ = _area.AddTickCallback(Ticked);
        }

        return done.Task;
    }

    /// <summary>
    /// The logical pixel at or before <paramref name="at"/> that falls on a whole pixel of the monitor's at
    /// <paramref name="scale"/>, as a compositor blurs a surface it places between two; at a scale none falls on, the one before.
    /// </summary>
    public static int OnPixel(double at, double scale)
    {
        var whole = (int)Math.Floor(at);
        for (var back = 0; back < Steps && whole - back > 0; back++)
        {
            if (Whole((whole - back) * scale))
            {
                return whole - back;
            }
        }

        return whole;
    }

    /// <summary>A window of thermi's that draws nothing of its own: no background, no shadow.</summary>
    public static void Unpainted(Gtk.Window window)
    {
        if (!_styled && Gdk.Display.GetDefault() is { } display)
        {
            // Only thermi's overlays draw nothing behind their pictures; GTK's other windows of thermi's keep their theme.
            var css = Gtk.CssProvider.New();
            css.LoadFromString($"window.{Style} {{ background: none; box-shadow: none; }}");
            Gtk.StyleContext.AddProviderForDisplay(display, css, 800);
            _styled = true;
        }

        window.AddCssClass(Style);
    }

    /// <summary>A window of thermi's that the pointer passes through everywhere.</summary>
    public static void TakeNoPointer(Gtk.Window window)
    {
        using var mask = new Cairo.ImageSurface(Cairo.Format.A8, 1, 1);
        using var region = Gdk.Functions.CairoRegionCreateFromSurface(mask);
        window.GetSurface()?.SetInputRegion(region);
    }

    public void Dispose()
    {
        Shown();
        Window.Destroy();
        Window.Dispose();
        _area.Dispose();
        _image?.Dispose();
    }

    // The fewest logical pixels that hold <paramref name="pixels"/> and are whole pixels of the monitor's, so the compositor
    // shows the surface's buffer one to one instead of scaling it.
    private static int Covering(int pixels, double scale)
    {
        var logical = (int)Math.Ceiling(pixels / scale);
        for (var more = 0; more < Steps; more++)
        {
            if (Whole((logical + more) * scale))
            {
                return logical + more;
            }
        }

        return logical;
    }

    private static bool Whole(double pixels) => Math.Abs(pixels - Math.Round(pixels)) < 1e-6;

    // The picture's pixels on the surface's own, whatever logical size GTK rounds the window to.
    private void Draw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
    {
        if (_image is null)
        {
            return;
        }

        var scale = Window.GetSurface()?.GetScale() ?? _scale;
        cr.Scale(1 / scale, 1 / scale);
        cr.SetSourceSurface(_image, _offset.X, _offset.Y);
        cr.Paint();
        // A layer surface takes its new size only once the compositor configures it, which a frame drawn before missed.
        _drawn = width == _area.GetContentWidth() && height == _area.GetContentHeight();
    }

    // GTK draws a frame after a tick of the frame clock, which ticks again only once the compositor took that frame.
    private bool Ticked(Gtk.Widget widget, Gdk.FrameClock clock)
    {
        if (!_drawn)
        {
            return true;
        }

        Shown();
        return false;
    }

    // What waits for the pictures shown may show others, which then wait for a frame of their own.
    private void Shown()
    {
        var waiting = _waiting.ToList();
        _waiting.Clear();
        foreach (var done in waiting)
        {
            done.TrySetResult();
        }
    }

    /// <summary>
    /// The boxes that take the pointer, in the window's logical pixels, each grown to whole ones; none under click-through,
    /// where the pointer passes to what is under the window everywhere.
    /// </summary>
    public IReadOnlyList<(int X, int Y, int Width, int Height)> Clicked()
    {
        var (x, y) = (_offset.X / _scale, _offset.Y / _scale);
        return [.. _clicks.Select(box =>
        {
            var (left, top) = ((int)Math.Floor(box.Left + x), (int)Math.Floor(box.Top + y));
            return (left, top, (int)Math.Ceiling(box.Left + box.Width + x) - left, (int)Math.Ceiling(box.Top + box.Height + y) - top);
        })];
    }

    /// <summary>Draws nothing from now on, while it stays to take the rest of a press that began on it.</summary>
    public void Blank() => _area.SetOpacity(0);

    /// <summary>Called each time the boxes that take the pointer change, as an X11 window's shape follows them.</summary>
    public Action Shaped { get; set; } = () => { };

    private void Region()
    {
        if (Window.GetSurface() is not { } surface)
        {
            return;
        }

        var (width, height) = (Math.Max(_area.GetContentWidth(), 1), Math.Max(_area.GetContentHeight(), 1));
        using var mask = new Cairo.ImageSurface(Cairo.Format.A8, width, height);
        using (var cr = new Cairo.Context(mask))
        {
            foreach (var (left, top, across, down) in Clicked())
            {
                cr.Rectangle(left, top, across, down);
            }

            cr.SetSourceRgba(0, 0, 0, 1);
            cr.Fill();
        }

        using var region = Gdk.Functions.CairoRegionCreateFromSurface(mask);
        surface.SetInputRegion(region);
        Shaped();
    }

    private void Crossed(Gtk.EventController crossing, string kind, (double X, double Y) at)
    {
        _heardAt = at;
        Pointer(new ShellPointer(_id, kind, Origin.X + at.X, Origin.Y + at.Y, 0, (uint)crossing.GetCurrentEventState(), crossing.GetCurrentEventTime(), 0, 0));
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

        // A turn of the wheel on Wayland tells no point: it is where the pointer last was on the window.
        var (x, y) = _heardAt = heard.GetPosition(out var px, out var py) ? (px, py) : _heardAt;
        if (!Kinds.TryGetValue(type, out var kind))
        {
            return false;
        }

        var button = type is Gdk.EventType.ButtonPress or Gdk.EventType.ButtonRelease ? Gdk.Internal.ButtonEvent.GetButton(heard.Handle) : 0;
        var (dx, dy) = type == Gdk.EventType.Scroll ? Turned(heard.Handle) : (0, 0);
        Pointer(new ShellPointer(_id, kind, Origin.X + x, Origin.Y + y, button, (uint)heard.GetModifierState(), heard.GetTime(), dx, dy));
        return false;
    }

    // A wheel's notch as a turn of one, up negative, as the extension tells it; a smooth scroll as its deltas.
    private static (double, double) Turned(nint scroll)
    {
        var direction = Gdk.Internal.ScrollEvent.GetDirection(scroll);
        if (direction == Gdk.ScrollDirection.Smooth)
        {
            Gdk.Internal.ScrollEvent.GetDeltas(scroll, out var x, out var y);
            return (x, y);
        }

        return direction == Gdk.ScrollDirection.Up ? (0, -1) : direction == Gdk.ScrollDirection.Down ? (0, 1) : (0, 0);
    }
}
