using Thermi.App.Gestures;
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
internal sealed record ShellPaint(ThermiConfig Config, ConfigProblem? Problem, ShellScreens Screens, TrayInput Input, HighContrastColors? HighContrast);

/// <summary>What the Shell was last sent of a picture besides its pixels and boxes: its size, scale and place on the stage.</summary>
internal readonly record struct ShownAs(int Width, int Height, int Stride, double Scale, double X, double Y);

/// <summary>
/// One overlay through the Shell's extension (docs/adr/0019-linux.md): it paints with Cairo at its monitor's scale, places
/// itself where Core's snaps and the taskbar row say, in thermi's pixels, and takes the pointer only where it draws. A
/// drag moves it at once, through the extension, and its drop is written as Windows' is, a snap mark in reach snapping it.
/// </summary>
internal sealed class ShellOverlay(int index, OverlayParts parts) : IDisposable
{
    // Clutter's Alt: held through a drop, the overlay goes where it is, whatever snap is in reach.
    private const uint Alt = 1 << 3;

    private readonly Painter _painter = new(reuse: true);

    // What the Shell shows of the overlay now, so a paint that changes nothing of it sends nothing.
    private GLib.Bytes? _pixels;
    private ShownAs? _shownAs;
    private IReadOnlyList<PillBox> _clicks = [];
    private ShellMarks? _marks;
    private (int Width, int Height) _size;
    private Press? _press;
    private (uint Time, Position At)? _clicked;
    private Position? _at;
    private double _monitor = 1;

    // What the last paint drew its items with, and its pixels' to thermi's; none while it stood for the stopped sensors.
    private (IReadOnlyList<OverlayItem> Items, OverlayLook Look, double Scale, double ToThermi)? _painted;

    /// <summary>Where the overlay shows, in thermi's pixels; null while hidden.</summary>
    public PixelRect? Box { get; private set; }

    /// <summary>Where a drag in progress has the overlay, past the drag distance, so a repaint leaves it there.</summary>
    public Position? Dragged { get; private set; }

    /// <summary>Whether a press holds the overlay now, which only its release or Esc ends.</summary>
    public bool Pressed => _press is not null;

    /// <summary>Paints the overlay and shows it where it goes; done once the Shell shows it.</summary>
    public Task Show(ShellPaint scene, IReadOnlyList<PixelRect?> shown)
    {
        var config = scene.Config;
        var overlay = config.Overlays[index];
        var screens = scene.Screens.Screens;
        var monitor = Snaps.ScreenOf(overlay, screens)?.Scale ?? scene.Screens.Scale;
        var drawn = ThermiMark.Overlay(config, index, scene.Input, windowOpenedOnce: true);
        var texts = scene.Input.Texts;
        var items = drawn.WithProblem(scene.Problem, texts);
        var look = OverlayLook.Of(overlay.Style, Desktop.DesktopLooks.LightPanels, scene.HighContrast);
        var scale = monitor * Snaps.SizeShown(overlay, screens);
#pragma warning disable CA2000 // The painter reuses the surface while its size holds, and disposes it itself.
        var picture = drawn.SensorsStopped
            ? _painter.PaintStopped(texts.Of(OverlayText.SensorsStopped), look, scale, items)
            : _painter.Paint(items, look, scale);
#pragma warning restore CA2000

        // A click takes the overlay only on its pills, and passes through to what is under it where it draws nothing.
        IReadOnlyList<PillBox> clicks = overlay.ClickThrough
            ? []
            : [.. _painter.Clickable.Select(box => new PillBox(box.Left / monitor, box.Top / monitor, box.Width / monitor, box.Height / monitor))];
        var toThermi = scene.Screens.Scale / monitor;
        _painted = drawn.SensorsStopped ? null : (items, look, scale, toThermi);
        _monitor = monitor;
        _size = ((int)Math.Round(picture.Width * toThermi), (int)Math.Round(picture.Height * toThermi));
        var at = Dragged ?? TaskbarRow.Locate(config.Overlays, index, screens, _size, shown, parts.Time.GetElapsedTime(parts.Started)) ?? new Position(0, 0);
        _at = at;
        Box = new PixelRect(at.X, at.Y, at.X + _size.Width, at.Y + _size.Height);
        var (x, y) = scene.Screens.Stage(at);
        var shownAs = new ShownAs(picture.Width, picture.Height, picture.Stride, monitor, x, y);
        if (_pixels is { } last && _shownAs == shownAs && _clicks.SequenceEqual(clicks)
            && last.GetRegionSpan<byte>(0, last.GetSize()).SequenceEqual(picture.Pixels))
        {
            return Task.CompletedTask;
        }

        _pixels?.Dispose();
        _pixels = picture.Bytes();
        (_shownAs, _clicks) = (shownAs, clicks);
        return parts.Call(parts.Link.Show((uint)index, new ShellPicture(_pixels, picture.Width, picture.Height, picture.Stride, monitor, clicks), x, y));
    }

    /// <summary>The Shell shows nothing of the overlay's now, as when the extension came again: the next paint sends it whole.</summary>
    public void Forget()
    {
        _pixels?.Dispose();
        (_pixels, _shownAs, _clicks) = (null, null, []);
    }

    /// <summary>
    /// "Separate" from the overlay's menu, as one gesture: the pill lands as its own overlay, at its size, beside this one
    /// as the rest of its items will draw it; nothing before the overlay first painted.
    /// </summary>
    public void Separate(string pill, ShellScreens screens)
    {
        if (Box is not { } shown || _painted is not { } painted)
        {
            return;
        }

        var settings = parts.Settings;
        var ids = DesktopGestures.PillItems(settings.Config, pill);
        var drop = DesktopGestures.Separate(
            settings.Config, index, pill, shown, Measured(each => ids.Contains(each.Id)), Measured(each => !ids.Contains(each.Id)), screens.Screens);
        if (DropWrite.Write(settings, index, drop, parts.Settle, move: null, parts.Gestures, OverlayText.UndoSeparate))
        {
            parts.RequestPaint();
        }

        (int Width, int Height) Measured(Func<OverlayItem, bool> keep)
        {
            var (width, height) = _painter.Measure([.. painted.Items.Where(keep)], painted.Look, painted.Scale);
            return ((int)Math.Round(width * painted.ToThermi), (int)Math.Round(height * painted.ToThermi));
        }
    }

    /// <summary>Takes the overlay off the screen; done once the Shell did.</summary>
    public Task Hide()
    {
        if (Box is null)
        {
            return Task.CompletedTask;
        }

        (Box, _press, Dragged) = (null, null, null);
        Unmark();
        Forget();
        return parts.Call(parts.Link.Hide((uint)index));
    }

    /// <summary>The pointer on the overlay: a press holds it, a move past the drag distance drags it, and the release drops it.</summary>
    public void Pointer(ShellPointer pointer, ShellScreens screens)
    {
        var cursor = screens.Thermi(pointer.X, pointer.Y);
        var config = parts.Settings.Config;
        if (index >= config.Overlays.Count || _at is not { } corner)
        {
            return;
        }

        switch (pointer.Kind)
        {
            case "press" when pointer.Button == 1 && Twice(pointer.Time, cursor, screens):
                (_press, _clicked) = (null, null);
                parts.OpenSettings(index, ItemAt(cursor, screens));
                break;
            case "press" when pointer.Button == 1:
                (_press, Dragged, _clicked) = (new Press(cursor, corner), null, (pointer.Time, cursor));
                break;
            case "press" when pointer.Button == 3:
                parts.Menu(index, pointer.X, pointer.Y, ItemAt(cursor, screens));
                break;
            case "motion" when _press is { } press:
                Dragged = Snaps.Dragged(press, cursor, Distance(screens), config.Overlays[index].Locked);
                var (x, y) = screens.Stage(Dragged ?? press.Overlay);
                Mark(screens, config.Overlays[index].Style.Orientation, free: (pointer.Modifiers & Alt) != 0);
                MoveTo(x, y);
                break;
            case "release" when _press is { } press && pointer.Button == 1:
                var dropped = Dragged;
                (_press, Dragged) = (null, null);
                Unmark();
                parts.Dropped(dropped is { } at && Drop(press, cursor, at, screens, free: (pointer.Modifiers & Alt) != 0));
                break;
            default:
                break;
        }
    }

    /// <summary>Esc while the overlay is held: the drag ends where it began, and nothing is written.</summary>
    public void Cancel(ShellScreens screens)
    {
        if (_press is not { } press)
        {
            return;
        }

        (_press, Dragged) = (null, null);
        Unmark();
        var (x, y) = screens.Stage(press.Overlay);
        MoveTo(x, y);
        parts.Dropped(false);
    }

    /// <summary>The overlay's size in thermi's pixels at a size and look of its settings, as the taskbar row measures it.</summary>
    public (int Width, int Height) Measure(ShellPaint scene, OverlayConfig overlay)
    {
        var screens = scene.Screens.Screens;
        var monitor = Snaps.ScreenOf(overlay, screens)?.Scale ?? scene.Screens.Scale;
        var drawn = ThermiMark.Overlay(scene.Config, index, scene.Input, windowOpenedOnce: true);
        var look = OverlayLook.Of(overlay.Style, Desktop.DesktopLooks.LightPanels, scene.HighContrast);
        var (width, height) = _painter.Measure(drawn.Items, look, monitor * Snaps.SizeShown(overlay, screens));
        var toThermi = scene.Screens.Scale / monitor;
        return ((int)Math.Round(width * toThermi), (int)Math.Round(height * toThermi));
    }

    public void Dispose()
    {
        Forget();
        _painter.Dispose();
    }

    // Past the drag distance, the spots in reach show, the one a drop here snaps to filled; Alt held lights none.
    private void Mark(ShellScreens screens, Orientation orientation, bool free)
    {
        if (Dragged is not { } at)
        {
            return;
        }

        _marks ??= new ShellMarks(parts, screens, orientation, _size);
        _marks.Light(Snaps.Near(screens.Screens, orientation, _size.Width, _size.Height, at, magnets: !free));
    }

    private void Unmark()
    {
        _marks?.Hide();
        _marks = null;
    }

    private void MoveTo(double x, double y)
    {
        _shownAs = _shownAs is { } shown ? shown with { X = x, Y = y } : null;
        _ = parts.Call(parts.Link.Move((uint)index, x, y));
    }

    private bool Twice(uint time, Position cursor, ShellScreens screens)
    {
        var near = Distance(screens);
        return _clicked is var (first, at) && time - first <= parts.Pointer.DoubleClick
            && Math.Abs(cursor.X - at.X) <= near.Width && Math.Abs(cursor.Y - at.Y) <= near.Height;
    }

    private DragDistance Distance(ShellScreens screens)
    {
        var pixels = (int)Math.Round(parts.Pointer.DragThreshold * screens.Scale);
        return new(pixels, pixels);
    }

    // The places are in the picture's own pixels, at its monitor's scale.
    private string? ItemAt(Position cursor, ShellScreens screens) =>
        _at is { } at ? _painter.Places.At((cursor.X - at.X) * _monitor / screens.Scale, (cursor.Y - at.Y) * _monitor / screens.Scale)?.Id : null;

    // A drop near a snap mark snaps there, else the overlay goes where it was let go, inside its monitor's work area; the
    // plain move through the settings' release, so it stays where it was dropped until written. True when it wrote them.
    private bool Drop(Press press, Position cursor, Position at, ShellScreens screens, bool free)
    {
        var before = parts.Settings.Config;
        var orientation = before.Overlays[index].Style.Orientation;
        var kept = Snaps.Inside(at, screens.Screens, _size.Width, _size.Height);
        var mark = Snaps.Near(screens.Screens, orientation, _size.Width, _size.Height, at, magnets: !free);
        var drop = DesktopGestures.Release(before, index, null, kept, default, mark);
        var move = new PlainMove(press, new Position(cursor.X + kept.X - at.X, cursor.Y + kept.Y - at.Y), Distance(screens));
        return DropWrite.Write(parts.Settings, index, drop, parts.Settle, move, parts.Gestures);
    }
}
