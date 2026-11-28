using Thermi.App.Gestures;
using Thermi.Core.Look;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Desktop;

/// <summary>
/// A GTK window that shows one of thermi's pictures pixel for pixel, as a layer surface or an X11 window makes it: no
/// frame, nothing drawn where the picture draws nothing, and the pointer taken only on the picture's boxes. It tells the
/// pointer and Esc on it in the stage's logical pixels (<see cref="PicturePointer"/>), from where its surface's corner is
/// on the stage. A request to close it, from the desktop, goes to <see cref="Closed"/>, and the window stays.
/// </summary>
internal sealed class PictureWindow : IDisposable
{
    private const string Style = "thermi-overlay";

    // Eighths, as compositors keep Wayland's fractional scales of 120ths to quarters or eighths: a whole pixel within 8 logical ones.
    private const int Steps = 8;

    private static bool _styled;

    private readonly Microsoft.Extensions.Logging.ILogger _log;
    private readonly Gtk.DrawingArea _area = Gtk.DrawingArea.New();
    private readonly List<TaskCompletionSource> _waiting = [];
    private readonly PicturePointer _pointer;
    private bool _drawn;
    private Cairo.ImageSurface? _image;
    private (int X, int Y) _offset;
    private (double X, double Y)? _moved;
    private double _scale = 1;
    private IReadOnlyList<PillBox> _clicks = [];
    private Action _unmenu = () => { };

    /// <summary>The window of picture <paramref name="id"/>; a leave during a press loses it where <paramref name="leaveLoses"/>.</summary>
    public PictureWindow(uint id, Action<Gtk.Window> made, bool leaveLoses, Microsoft.Extensions.Logging.ILogger log)
    {
        Unpainted(Window);
        _log = log;
        MainLoopRelease.Watch(log, Window, _area);
        Window.SetDecorated(false);
        Window.SetResizable(false);
        Window.SetTitle("thermi");
        _area.SetDrawFunc(Draw);
        Window.SetChild(_area);
        _pointer = new PicturePointer(id, Window, () => Origin, leaveLoses);
        Window.OnRealize += (_, _) => Region();
        Window.OnCloseRequest += (_, _) =>
        {
            Closed();
            return true;
        };
        made(Window);
    }

    /// <summary>Where the window's corner is on the stage, in logical pixels, from which its pointer is told.</summary>
    public (double X, double Y) Origin { get; set; }

    // As a layer surface's margins, the corner moves with the next frame: the pointer is told from there once the compositor
    // took it, so a drag counts no move before it happened.
    public void MoveTo((double X, double Y) origin) => (_moved, Origin) = _waiting.Count > 0 ? (origin, Origin) : (((double X, double Y)?)null, origin);

    public Gtk.Window Window { get; } = Gtk.Window.New();

    public Action<ShellPointer> Pointer { get => _pointer.Pointer; set => _pointer.Pointer = value; }

    public Action<uint, string> Key { get => _pointer.Key; set => _pointer.Key = value; }

    /// <summary>A press on the window lost the pointer to another grab.</summary>
    public Action<uint> Lost { get => _pointer.Lost; set => _pointer.Lost = value; }

    /// <summary>The desktop asked the window to close, as a window manager's close does on X11, or a compositor that closed its surface.</summary>
    public Action Closed { get; set; } = () => { };

    /// <summary>Called each time the boxes that take the pointer change, as an X11 window's shape follows them.</summary>
    public Action Shaped { get; set; } = () => { };

    /// <summary>The picture's opacity, 1 whole.</summary>
    public double Opacity => _area.GetOpacity();

    /// <summary>
    /// Shows <paramref name="picture"/> <paramref name="offset"/> pixels in from the window's corner, so it lands on a
    /// pixel of its own where the window's corner can only be on a logical one; done once the compositor showed it. The
    /// picture's pixels are copied: the caller keeps its own.
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
        return Showing(offset);
    }

    /// <summary>Shows what <paramref name="other"/> shows, its pixels copied, as a picture goes on to another window.</summary>
    public Task ShowLike(PictureWindow other, (int X, int Y) offset)
    {
        if (other._image is not { } image)
        {
            return Task.CompletedTask;
        }

        _image?.Dispose();
        _image = new Cairo.ImageSurface(Cairo.Format.Argb32, image.Width, image.Height);
        using (var cr = new Cairo.Context(_image))
        {
            cr.SetSourceSurface(image, 0, 0);
            cr.Operator = Cairo.Operator.Source;
            cr.Paint();
        }

        _image.MarkDirty();
        (_scale, _clicks) = (other._scale, other._clicks);
        return Showing(offset);
    }

    private Task Showing((int X, int Y) offset)
    {
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
        return Drawn();
    }

    /// <summary>The picture at <paramref name="opacity"/>, 1 whole, as GTK draws it without a new one; done once it shows.</summary>
    public Task Fade(double opacity)
    {
        _area.SetOpacity(opacity);
        return _image is null ? Task.CompletedTask : Drawn();
    }

    /// <summary>Draws nothing from now on, while it stays to take the rest of a press that began on it.</summary>
    public void Blank() => _area.SetOpacity(0);

    /// <summary>The pointer over the window as <paramref name="shape"/> says, by the names every cursor theme has.</summary>
    public void Cursor(PointerShape shape) => Window.SetCursorFromName(shape switch
    {
        PointerShape.NotAllowed => "not-allowed",
        PointerShape.Resize => "nwse-resize",
        PointerShape.Default or _ => null,
    });

    /// <summary>Puts a PNG on the desktop's clipboard, which GTK serves to whoever pastes it while thermi runs.</summary>
    public static void Copy(byte[] png)
    {
        using var bytes = GLib.Bytes.New(png);
        using var texture = Gdk.Texture.NewFromBytes(bytes);
        Gdk.Display.GetDefault()?.GetClipboard().SetTexture(texture);
    }

    // Done once a frame drawn from now shows: GTK 4 draws a window's own opacity as whole, so the picture's area fades.
    private Task Drawn()
    {
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

    // The box on the window a menu points at: a popup is pointed only at its own surface, inside it.
    private Gdk.Rectangle Pointing(PillBox anchor)
    {
        var (width, height) = (Math.Max(_area.GetContentWidth(), 1), Math.Max(_area.GetContentHeight(), 1));
        var (left, top) = (
            Math.Clamp((int)Math.Round(anchor.Left - Origin.X), 0, width - 1), Math.Clamp((int)Math.Round(anchor.Top - Origin.Y), 0, height - 1));
        return new Gdk.Rectangle
        {
            X = left,
            Y = top,
            Width = Math.Clamp((int)Math.Round(anchor.Width), 1, width - left),
            Height = Math.Clamp((int)Math.Round(anchor.Height), 1, height - top),
        };
    }

    /// <summary>
    /// Shows a menu of <paramref name="rows"/> against <paramref name="anchor"/>, a box of the stage, on its
    /// <paramref name="side"/>, as GTK's own, in place of one open; <paramref name="closed"/> follows once it closes, chosen
    /// or not.
    /// </summary>
    public void Menu(IReadOnlyList<ShellRow> rows, PillBox anchor, Gtk.PositionType side, Action<int> chosen, Action closed)
    {
        _unmenu();
        var menu = PictureMenu.New(rows, chosen);
        menu.SetParent(_area);
        using var at = Pointing(anchor);
        menu.SetPointingTo(at);
        menu.SetPosition(side);
        var open = true;
        void Unmenu()
        {
            if (open)
            {
                open = false;
                _unmenu = () => { };
                menu.Popdown();
                menu.Unparent();
                MainLoopRelease.Hold(_log, menu);
                menu.Dispose();
            }
        }

        menu.OnClosed += (_, _) =>
        {
            // One shown in its place, or the window going, closes it without a word.
            if (open)
            {
                closed();
                // GTK is still in the menu's own handlers as it closes.
                GLib.Functions.IdleAdd(0, () =>
                {
                    Unmenu();
                    return false;
                });
            }
        };
        _unmenu = Unmenu;
        menu.Popup();
    }

    public void Dispose()
    {
        _unmenu();
        Shown();
        Window.Destroy();
        MainLoopRelease.Hold(_log, Window, _area);
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
        (Origin, _moved) = (_moved ?? Origin, null);
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
}
