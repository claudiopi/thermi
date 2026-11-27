using Thermi.App.Gestures;
using Thermi.App.Logging;
using Thermi.Core.Look;
using Thermi.Core.Painting;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;
using Thermi.Linux.Painting;

namespace Thermi.Linux.Overlay;

/// <summary>What every overlay paints from at a paint: the settings, the monitors, the readings and the desktop's contrast.</summary>
internal sealed record OverlayPaint(ThermiConfig Config, ConfigProblem? Problem, ShellScreens Screens, TrayInput Input, HighContrastColors? HighContrast);

/// <summary>What the surfaces were last sent of a picture besides its pixels and boxes: its size, scale and place on the stage.</summary>
internal readonly record struct ShownAs(int Width, int Height, int Stride, double Scale, double X, double Y);

/// <summary>What the last paint drew an overlay's items with, and its pixels' to thermi's.</summary>
internal sealed record PaintedLook(IReadOnlyList<OverlayItem> Items, OverlayLook Look, double Scale, double ToThermi);

/// <summary>
/// One overlay through the desktop's surfaces (docs/adr/0019-linux.md): it paints with Cairo at its monitor's scale,
/// places itself where Core's snaps and the taskbar row say, in thermi's pixels, takes the pointer only where it draws, and
/// fades near the pointer where the surfaces tell it. Its pointer's gestures are <see cref="OverlayPointer"/>'s.
/// </summary>
internal sealed class DesktopOverlay : IDisposable
{
    private readonly int _index;
    private readonly OverlayParts _parts;
    private readonly Painter _painter = new(reuse: true);

    // What the surfaces show of the overlay now, so a paint that changes nothing of it sends nothing, and is done once
    // what was sent shows, as a drop whose picture its drag sent last is.
    private GLib.Bytes? _pixels;
    private ShownAs? _shownAs;
    private IReadOnlyList<PillBox> _clicks = [];
    private Task _showing = Task.CompletedTask;

    // The fade the surfaces show the overlay at, in 255ths as the Shell's actors keep it, and the scale it fades by.
    private int _alpha = Opaque;
    private double _fadeScale = 1;

    public DesktopOverlay(int index, OverlayParts parts)
    {
        (_index, _parts) = (index, parts);
        Pointer = new OverlayPointer(index, parts, this);
    }

    private const int Opaque = 255;

    public OverlayPointer Pointer { get; }

    /// <summary>Where the overlay shows, in thermi's pixels; null while hidden.</summary>
    public PixelRect? Box { get; private set; }

    /// <summary>The overlay's corner as it last showed, in thermi's pixels.</summary>
    public Position? At { get; private set; }

    /// <summary>The overlay's size in thermi's pixels as it last showed.</summary>
    public (int Width, int Height) Size { get; private set; }

    /// <summary>Its monitor's scale, the picture's pixels to a logical one.</summary>
    public double Monitor { get; private set; } = 1;

    /// <summary>What the last paint drew, none while it stood for the stopped sensors.</summary>
    public PaintedLook? Painted { get; private set; }

    public IReadOnlyList<PaintedItem> Places => _painter.Places;

    /// <summary>Paints the overlay and shows it where it goes; done once the surfaces show it.</summary>
    public Task Show(OverlayPaint scene, IReadOnlyList<PixelRect?> shown)
    {
        var config = scene.Config;
        var overlay = Pointer.Drag?.Shown(config.Overlays[_index]) ?? config.Overlays[_index];
        var screens = scene.Screens.Screens;
        var monitor = Snaps.ScreenOf(overlay, screens)?.Scale ?? scene.Screens.Scale;
        var drawn = ThermiMark.Overlay(config, _index, scene.Input, windowOpenedOnce: true);
        var texts = scene.Input.Texts;
        var items = drawn.WithProblem(scene.Problem, texts);
        var look = OverlayLook.Of(overlay.Style, Desktop.DesktopLooks.LightPanels, scene.HighContrast);
        var scale = monitor * Snaps.SizeShown(overlay, screens);
#pragma warning disable CA2000 // The painter reuses the surface while its size holds, and disposes it itself.
        var picture = drawn.SensorsStopped
            ? _painter.PaintStopped(texts.Of(OverlayText.SensorsStopped), look, scale, items)
            : _painter.Paint(items, look, scale);
#pragma warning restore CA2000
        var toThermi = scene.Screens.Scale / monitor;
        Painted = drawn.SensorsStopped ? null : new PaintedLook(items, look, scale, toThermi);
        Mark(picture, look, scale);

        // A click takes the overlay only on its pills, and passes through to what is under it where it draws nothing.
        IReadOnlyList<PillBox> clicks = overlay.ClickThrough
            ? []
            : [.. _painter.Clickable.Select(box => new PillBox(box.Left / monitor, box.Top / monitor, box.Width / monitor, box.Height / monitor))];
        Monitor = monitor;
        Size = ((int)Math.Round(picture.Width * toThermi), (int)Math.Round(picture.Height * toThermi));
        var at = Pointer.HeldAt
            ?? TaskbarRow.Locate(config.Overlays, _index, screens, Size, shown, _parts.Time.GetElapsedTime(_parts.Started))
            ?? new Position(0, 0);
        At = at;
        Box = new PixelRect(at.X, at.Y, at.X + Size.Width, at.Y + Size.Height);
        _fadeScale = scale * toThermi;
        var sent = Send(picture, scene.Screens.Stage(at), clicks);
        _ = Fade(overlay);
        return sent;
    }

    /// <summary>
    /// The overlay's fade near the pointer, set on its surface, never a paint, and only when it changed: done once the
    /// surfaces show it.
    /// </summary>
    public Task Fade(OverlayConfig overlay)
    {
        var opacity = Box is { } box && _parts.Cursor is { } cursor
            ? PointerFade.Opacity(overlay, box, cursor.At, _fadeScale, windowOpen: false, Pointer.Holds)
            : 1;
        var alpha = (int)Math.Round(opacity * Opaque);
        if (alpha == _alpha)
        {
            return Task.CompletedTask;
        }

        var sent = _parts.Call(_parts.Surfaces.Fade((uint)_index, (double)alpha / Opaque));
        if (_alpha == Opaque)
        {
            _parts.Trace(TraceEvents.Faded, sent);
        }

        _alpha = alpha;
        return sent;
    }

    /// <summary>The surfaces show nothing of the overlay's now, as when they came again: the next paint sends it whole.</summary>
    public void Forget()
    {
        _pixels?.Dispose();
        (_pixels, _shownAs, _clicks, _alpha) = (null, null, [], Opaque);
    }

    /// <summary>
    /// "Separate" from the overlay's menu, as one gesture: the pill lands as its own overlay, at its size, beside this one
    /// as the rest of its items will draw it; nothing before the overlay first painted.
    /// </summary>
    public void Separate(string pill, ShellScreens screens)
    {
        if (Box is not { } shown || Painted is not { } painted)
        {
            return;
        }

        var settings = _parts.Settings;
        var ids = DesktopGestures.PillItems(settings.Config, pill);
        var drop = DesktopGestures.Separate(
            settings.Config, _index, pill, shown, Measured(each => ids.Contains(each.Id)), Measured(each => !ids.Contains(each.Id)), screens.Screens);
        if (DropWrite.Write(settings, _index, drop, _parts.Settle, move: null, _parts.Gestures, OverlayText.UndoSeparate))
        {
            _parts.RequestPaint();
        }

        (int Width, int Height) Measured(Func<OverlayItem, bool> keep)
        {
            var (width, height) = _painter.Measure([.. painted.Items.Where(keep)], painted.Look, painted.Scale);
            return ((int)Math.Round(width * painted.ToThermi), (int)Math.Round(height * painted.ToThermi));
        }
    }

    /// <summary>Takes the overlay off the screen; done once the surfaces did.</summary>
    public Task Hide()
    {
        if (Box is null)
        {
            return Task.CompletedTask;
        }

        Box = null;
        Pointer.Let();
        Forget();
        return _parts.Call(_parts.Surfaces.Hide((uint)_index));
    }

    /// <summary>The overlay's size in thermi's pixels at a size and look of its settings, as the taskbar row measures it.</summary>
    public (int Width, int Height) Measure(OverlayPaint scene, OverlayConfig overlay)
    {
        var screens = scene.Screens.Screens;
        var monitor = Snaps.ScreenOf(overlay, screens)?.Scale ?? scene.Screens.Scale;
        var drawn = ThermiMark.Overlay(scene.Config, _index, scene.Input, windowOpenedOnce: true);
        var look = OverlayLook.Of(overlay.Style, Desktop.DesktopLooks.LightPanels, scene.HighContrast);
        var (width, height) = _painter.Measure(drawn.Items, look, monitor * Snaps.SizeShown(overlay, screens));
        var toThermi = scene.Screens.Scale / monitor;
        return ((int)Math.Round(width * toThermi), (int)Math.Round(height * toThermi));
    }

    /// <summary>Where a drop at a point of the screen joins this overlay, as <see cref="PaintedPlaces.JoinAt"/> says.</summary>
    public DropTarget JoinAt(Position cursor, string? alone) => OnPicture(cursor) is var (x, y) && Painted is { } painted
        ? Places.JoinAt(_index, x, y, alone, painted.Look.Orientation == Orientation.Vertical)
        : default;

    /// <summary>A point of the screen in the picture's own pixels, or null off the overlay.</summary>
    public (double X, double Y)? OnPicture(Position cursor) => At is { } at && Painted is { } painted
        ? ((cursor.X - at.X) / painted.ToThermi, (cursor.Y - at.Y) / painted.ToThermi)
        : null;

    /// <summary>The overlay follows a drag, its corner to <paramref name="at"/>, in thermi's pixels.</summary>
    public void MoveTo(Position at, ShellScreens screens)
    {
        var (x, y) = screens.Stage(at);
        _shownAs = _shownAs is { } shown ? shown with { X = x, Y = y } : null;
        _ = _parts.Call(_parts.Surfaces.Move((uint)_index, x, y));
    }

    /// <summary>
    /// The pill or item a hold carries off, as it shows alone over the others, drawn for a monitor at
    /// <paramref name="monitor"/>, in its pixels.
    /// </summary>
    public Picture Carried(string item, bool alone, double monitor)
    {
        var painted = Painted!;
        var ids = DesktopGestures.PillItems(_parts.Settings.Config, item);
        var label = painted.Items.FirstOrDefault(each => ids.Contains(each.Id) && each.Label is not null)?.Label;
        IReadOnlyList<OverlayItem> items = alone
            ? [.. painted.Items.Where(each => each.Id == item).Select(each => each with { Label = label })]
            : [.. painted.Items.Where(each => ids.Contains(each.Id))];
        // Carried over other pills, it keeps a background of its own whatever the look's opacity.
        using var painter = new Painter();
        return painter.Paint(items, painted.Look with { Orientation = Orientation.Horizontal, Background = 1, Opacity = 1 }, painted.Scale / Monitor * monitor);
    }

    public void Dispose()
    {
        Forget();
        Pointer.Dispose();
        _painter.Dispose();
    }

    // The gap a lifted pill leaves, and where a drop onto this overlay joins it.
    private void Mark(Picture picture, OverlayLook look, double scale)
    {
        if (Painted is null)
        {
            return;
        }

        if (Pointer.Drag is { Lifted: true } drag)
        {
            DropMarks.Gap(picture, new PillBox(drag.Gap.X, drag.Gap.Y, drag.Gap.Width, drag.Gap.Height), scale);
        }

        var preview = _parts.Preview;
        if (preview.Onto == _index)
        {
            var within = preview.InPill ? Pointer.Drag?.Carried?.Item : null;
            DropMarks.Join(picture, Places, (preview.Before, within), look.Orientation == Orientation.Vertical, _parts.Accent, scale);
        }
    }

    private Task Send(Picture picture, (double X, double Y) stage, IReadOnlyList<PillBox> clicks)
    {
        var shownAs = new ShownAs(picture.Width, picture.Height, picture.Stride, Monitor, stage.X, stage.Y);
        if (_pixels is { } last && _shownAs == shownAs && _clicks.SequenceEqual(clicks)
            && last.GetRegionSpan<byte>(0, last.GetSize()).SequenceEqual(picture.Pixels))
        {
            return _showing;
        }

        _pixels?.Dispose();
        _pixels = picture.Bytes();
        (_shownAs, _clicks) = (shownAs, clicks);
        var shown = new ShellPicture(_pixels, picture.Width, picture.Height, picture.Stride, Monitor, clicks);
        _showing = _parts.Call(_parts.Surfaces.Show((uint)_index, shown, stage.X, stage.Y));
        return _showing;
    }
}
