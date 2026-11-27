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

    // The pointer's events as the extension names them.
    private static readonly Dictionary<Gdk.EventType, string> Kinds = new()
    {
        [Gdk.EventType.ButtonPress] = "press",
        [Gdk.EventType.ButtonRelease] = "release",
        [Gdk.EventType.MotionNotify] = "motion",
        [Gdk.EventType.LeaveNotify] = "leave",
        [Gdk.EventType.Scroll] = "scroll",
    };

    private static bool _styled;

    private readonly uint _id;
    private readonly Gtk.DrawingArea _area = Gtk.DrawingArea.New();
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
        // GirCore cannot read GdkEvent, a fundamental type, out of the signal's values; the controller hands it out as it is.
        events.OnEvent += (controller, _) => controller.GetCurrentEvent() is { } heard && Heard(heard);
        Window.AddController(events);
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
    /// pixel of its own where the window's corner can only be on a logical one.
    /// </summary>
    public void Show(ShellPicture picture, (int X, int Y) offset)
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
        Place(offset);
        if (!Window.GetVisible())
        {
            Window.Present();
        }
    }

    /// <summary>The picture shown <paramref name="offset"/> pixels in from the window's corner, as it moves.</summary>
    public void Place((int X, int Y) offset)
    {
        if (_image is null)
        {
            return;
        }

        _offset = offset;
        _area.SetContentWidth((int)Math.Ceiling((_image.Width + offset.X) / _scale));
        _area.SetContentHeight((int)Math.Ceiling((_image.Height + offset.Y) / _scale));
        Region();
        _area.QueueDraw();
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
        Window.Destroy();
        Window.Dispose();
        _area.Dispose();
        _image?.Dispose();
    }

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
    }

    // The pointer passes to what is under the window everywhere but on the boxes; none takes it under click-through.
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
            var (x, y) = (_offset.X / _scale, _offset.Y / _scale);
            foreach (var box in _clicks)
            {
                var (left, top) = (Math.Floor(box.Left + x), Math.Floor(box.Top + y));
                cr.Rectangle(left, top, Math.Ceiling(box.Left + box.Width + x) - left, Math.Ceiling(box.Top + box.Height + y) - top);
            }

            cr.SetSourceRgba(0, 0, 0, 1);
            cr.Fill();
        }

        using var region = Gdk.Functions.CairoRegionCreateFromSurface(mask);
        surface.SetInputRegion(region);
    }

    private bool Heard(Gdk.Event heard)
    {
        var type = heard.GetEventType();
        if (type == Gdk.EventType.KeyPress && heard is Gdk.KeyEvent key && key.GetKeyval() == Escape)
        {
            Key(_id, "Escape");
            return true;
        }

        if (!Kinds.TryGetValue(type, out var kind) || !heard.GetPosition(out var x, out var y))
        {
            return false;
        }

        var button = heard is Gdk.ButtonEvent pressed ? pressed.GetButton() : 0;
        var (dx, dy) = heard is Gdk.ScrollEvent scroll ? Turned(scroll) : (0, 0);
        Pointer(new ShellPointer(_id, kind, Origin.X + x, Origin.Y + y, button, (uint)heard.GetModifierState(), heard.GetTime(), dx, dy));
        return false;
    }

    // A wheel's notch as a turn of one, up negative, as the extension tells it; a smooth scroll as its deltas.
    private static (double, double) Turned(Gdk.ScrollEvent scroll)
    {
        var direction = scroll.GetDirection();
        if (direction == Gdk.ScrollDirection.Smooth)
        {
            scroll.GetDeltas(out var x, out var y);
            return (x, y);
        }

        return direction == Gdk.ScrollDirection.Up ? (0, -1) : direction == Gdk.ScrollDirection.Down ? (0, 1) : (0, 0);
    }
}
