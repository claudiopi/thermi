using Thermi.Core.Editing;
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
internal sealed record OverlayScene(ThermiConfig Config, ConfigProblem? Problem, ShellScreens Screens, TrayInput Input, HighContrastColors? HighContrast);

/// <summary>
/// One overlay through the Shell's extension (docs/adr/0019-linux.md): it paints with Cairo at its monitor's scale, places
/// itself where Core's snaps and the taskbar row say, in thermi's pixels, and takes the pointer only where it draws. A
/// drag moves it at once, through the extension, and its drop is written as Windows' is, a snap mark in reach snapping it.
/// </summary>
internal sealed class ShellOverlay(int index, OverlayParts parts) : IDisposable
{
    // GNOME's own drag threshold, in logical pixels, which the Shell keeps to itself.
    private const int DragThreshold = 8;

    // GNOME's own double-click time, in milliseconds, which the Shell keeps to itself too.
    private const uint DoubleClick = 400;

    // Clutter's Alt: held through a drop, the overlay goes where it is, whatever snap is in reach.
    private const uint Alt = 1 << 3;

    private readonly Painter _painter = new();
    private (int Width, int Height) _size;
    private Press? _press;
    private (uint Time, Position At)? _clicked;
    private Position? _at;
    private double _monitor = 1;

    /// <summary>Where the overlay shows, in thermi's pixels; null while hidden.</summary>
    public PixelRect? Box { get; private set; }

    /// <summary>Where a drag in progress has the overlay, past the drag distance, so a repaint leaves it there.</summary>
    public Position? Dragged { get; private set; }

    /// <summary>Whether a press holds the overlay now, which only its release or Esc ends.</summary>
    public bool Pressed => _press is not null;

    public void Show(OverlayScene scene, IReadOnlyList<PixelRect?> shown)
    {
        var config = scene.Config;
        var overlay = config.Overlays[index];
        var screens = scene.Screens.Screens;
        var monitor = Snaps.ScreenOf(overlay, screens)?.Scale ?? scene.Screens.Scale;
        var drawn = ThermiMark.Overlay(config, index, scene.Input, windowOpenedOnce: true);
        var texts = scene.Input.Texts;
        var error = texts.Of(OverlayText.ConfigError);
        IReadOnlyList<OverlayItem> items = scene.Problem is null
            ? drawn.Items
            : [.. drawn.Items, new OverlayItem(texts.Of(OverlayText.ConfigLabel), error, Level.Critical, error)];
        var look = OverlayLook.Of(overlay.Style, Desktop.DesktopLooks.LightPanels, scene.HighContrast);
        var scale = monitor * Snaps.SizeShown(overlay, screens);
        using var picture = drawn.SensorsStopped
            ? _painter.PaintStopped(texts.Of(OverlayText.SensorsStopped), look, scale, items)
            : _painter.Paint(items, look, scale);
        var toThermi = scene.Screens.Scale / monitor;
        _monitor = monitor;
        _size = ((int)Math.Round(picture.Width * toThermi), (int)Math.Round(picture.Height * toThermi));
        var at = Dragged ?? TaskbarRow.Locate(config.Overlays, index, screens, _size, shown, parts.Time.GetElapsedTime(parts.Started)) ?? new Position(0, 0);
        _at = at;
        Box = new PixelRect(at.X, at.Y, at.X + _size.Width, at.Y + _size.Height);
        var clicks = overlay.ClickThrough
            ? []
            : picture.Drawn().Select(box => new PillBox(box.Left / monitor, box.Top / monitor, box.Width / monitor, box.Height / monitor)).ToList();
        var (x, y) = scene.Screens.Stage(at);
        parts.Call(parts.Link.Show((uint)index, picture, x, y, monitor, clicks));
    }

    public void Hide()
    {
        if (Box is null)
        {
            return;
        }

        (Box, _press, Dragged) = (null, null, null);
        parts.Call(parts.Link.Hide((uint)index));
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
                parts.OpenSettings(index, null);
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
                parts.Call(parts.Link.Move((uint)index, x, y));
                break;
            case "release" when _press is { } press && pointer.Button == 1:
                var dropped = Dragged;
                (_press, Dragged) = (null, null);
                if (dropped is { } at)
                {
                    Drop(press, cursor, at, screens, free: (pointer.Modifiers & Alt) != 0);
                }

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
        var (x, y) = screens.Stage(press.Overlay);
        parts.Call(parts.Link.Move((uint)index, x, y));
        parts.RequestPaint();
    }

    /// <summary>The overlay's size in thermi's pixels at a size and look of its settings, as the taskbar row measures it.</summary>
    public (int Width, int Height) Measure(OverlayScene scene, OverlayConfig overlay)
    {
        var screens = scene.Screens.Screens;
        var monitor = Snaps.ScreenOf(overlay, screens)?.Scale ?? scene.Screens.Scale;
        var drawn = ThermiMark.Overlay(scene.Config, index, scene.Input, windowOpenedOnce: true);
        var look = OverlayLook.Of(overlay.Style, Desktop.DesktopLooks.LightPanels, scene.HighContrast);
        var (width, height) = _painter.Measure(drawn.Items, look, monitor * Snaps.SizeShown(overlay, screens));
        var toThermi = scene.Screens.Scale / monitor;
        return ((int)Math.Round(width * toThermi), (int)Math.Round(height * toThermi));
    }

    public void Dispose() => _painter.Dispose();

    // A second press soon after the first, where it was: a double-click, which opens the settings.
    private bool Twice(uint time, Position cursor, ShellScreens screens)
    {
        var near = Distance(screens);
        return _clicked is var (first, at) && time - first <= DoubleClick
            && Math.Abs(cursor.X - at.X) <= near.Width && Math.Abs(cursor.Y - at.Y) <= near.Height;
    }

    private static DragDistance Distance(ShellScreens screens) =>
        new((int)Math.Round(DragThreshold * screens.Scale), (int)Math.Round(DragThreshold * screens.Scale));

    // The places are in the picture's own pixels, at its monitor's scale.
    private string? ItemAt(Position cursor, ShellScreens screens) =>
        _at is { } at ? _painter.Places.At((cursor.X - at.X) * _monitor / screens.Scale, (cursor.Y - at.Y) * _monitor / screens.Scale)?.Id : null;

    // A drop near a snap mark snaps there, else the overlay goes where it was let go, inside its monitor's work area; the
    // plain move through the settings' release, so it stays where it was dropped until written.
    private void Drop(Press press, Position cursor, Position at, ShellScreens screens, bool free)
    {
        var settings = parts.Settings;
        var before = settings.Config;
        var orientation = before.Overlays[index].Style.Orientation;
        var kept = Snaps.Inside(at, screens.Screens, _size.Width, _size.Height);
        var mark = Snaps.Near(screens.Screens, orientation, _size.Width, _size.Height, at, magnets: !free);
        var drop = DesktopGestures.Release(before, index, null, kept, default, mark);
        var after = drop.Result.Refused is null ? parts.Settle(before, drop.Result.Config).Config : before;
        if (ConfigEdits.Replay(before, after) is not { } change)
        {
            parts.RequestPaint();
            return;
        }

        if (after.Overlays[index].Snap is null)
        {
            var released = new Position(cursor.X + kept.X - at.X, cursor.Y + kept.Y - at.Y);
            after = settings.Release(press, released, Distance(screens), index) ?? before;
        }
        else
        {
            settings.Edit(change);
        }

        parts.Gestures.Add(drop.Kind, before, after);
        parts.RequestPaint();
    }
}
