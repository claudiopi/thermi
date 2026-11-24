using System.Drawing;
using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Overlay;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Thermi.Gestures;

/// <summary>What a press on an overlay took hold of: its edge, which moves it, a pill, or a floating overlay's corner.</summary>
internal enum Held
{
    Edge,
    Pill,
    Corner,
}

/// <summary>
/// Where a press on an overlay began: what it held, the item of a pill held, whether Ctrl was down, where that pill sits
/// in the overlay's pixels, the overlay's corner on the screen, its size and scale, and a pill's own picture, which a drag
/// off carries.
/// </summary>
internal sealed record DragStart(Held Held, string? Pill, bool Ctrl, RectangleF PillBox, Position Origin, Size Size, float Scale, Func<Bitmap>? PillPicture);

/// <summary>
/// One press on an overlay, from its capture to its release, as a desktop gesture: the overlay moved over snap marks and
/// other overlays, a pill carried off as a picture of itself, or the corner's size previewed. Its release writes one
/// edit, offered for undo and told to the window; a click writes nothing.
/// </summary>
internal sealed class DesktopDrag : IDisposable
{
    private readonly int _overlay;
    private readonly Settings _settings;
    private readonly DragStart _start;
    private readonly IReadOnlyList<Screen> _screens;
    private readonly HWND _window;
    private readonly IReadOnlyList<int?> _rowWidths;
    private SnapMarks? _marks;
    private HWND _ghost;
    private Position? _dropped;

    // rowWidths: the width each overlay keeps on a taskbar, the dragged one's included, among which its taskbar spots are offered.
    public DesktopDrag(int overlay, Settings settings, DragStart start, IReadOnlyList<Screen> screens, HWND window, IReadOnlyList<int?> rowWidths)
    {
        (_overlay, _settings, _start, _screens, _window, _rowWidths) = (overlay, settings, start, screens, window, rowWidths);
        Moves = start.Held != Held.Corner && !DesktopGestures.TakesOff(settings.Config, overlay, start.Pill, start.Ctrl);
    }

    private RowContext Rows => new(_settings.Config.Overlays, _overlay, _rowWidths);

    /// <summary>Whether the drag moves the overlay itself, which a pill carried off or a corner held never do.</summary>
    public bool Moves { get; }

    /// <summary>The size and direction a corner's drag shows until it is let go.</summary>
    public (double Size, Orientation Orientation)? Preview { get; private set; }

    /// <summary>The overlay as the drag shows it: at its previewed size and direction while a corner is held.</summary>
    public OverlayConfig Shown(OverlayConfig overlay) =>
        Preview is var (size, orientation) ? overlay with { Size = size, Style = overlay.Style with { Orientation = orientation } } : overlay;

    /// <summary>Whether the overlay is moved over another, which a drop there merges into.</summary>
    public bool Merging { get; private set; }

    /// <summary>
    /// The press moved, with the overlay's corner where the drag has it, or null while still a click, and
    /// <paramref name="over"/> another overlay; true when the overlay must paint again.
    /// </summary>
    public bool Moved(Position? dragged, bool over = false)
    {
        _dropped = dragged;
        if (dragged is not { } at)
        {
            return false;
        }

        if (_start.Held == Held.Corner)
        {
            Preview = DesktopGestures.Corner(
                _settings.Config.Overlays[_overlay].Size,
                _start.Size.Width,
                _start.Size.Height,
                at.X - _start.Origin.X,
                at.Y - _start.Origin.Y);
            return true;
        }

        if (!Moves)
        {
            Carry(PillAt(at));
            return false;
        }

        // Its first move paints the overlay again, with the background a drag gives it.
        var orientation = _settings.Config.Overlays[_overlay].Style.Orientation;
        var changed = _marks is null || Merging != over;
        Merging = over;
        _marks ??= new SnapMarks(_screens, orientation, _start.Size, _start.Scale, _window, Rows);
        // Over another overlay the drop merges, wherever a snap would be, so no mark is lit.
        _marks.Light(over ? null : Snaps.Near(_screens, orientation, _start.Size.Width, _start.Size.Height, at, Rows));
        return changed;
    }

    /// <summary>
    /// The press let go: a drag writes its gesture as one edit, the plain move through <see cref="Settings.Release"/> so it
    /// stays where it was dropped until written. <paramref name="onto"/> is the other overlay under the cursor.
    /// </summary>
    public void Release(Press press, Position cursor, DragDistance distance, int? onto, GestureUndo? undo)
    {
        if (_dropped is not { } at)
        {
            return;
        }

        var before = _settings.Config;
        var drop = _start.Held == Held.Corner
            ? new DesktopDrop(GestureKind.Resize, OverlayEdits.Resize(before, _overlay, Preview!.Value.Size, Preview.Value.Orientation))
            : DesktopGestures.Release(
                before,
                _overlay,
                Moves ? null : _start.Pill,
                Moves ? Kept(at) : PillAt(at),
                onto,
                Moves
                    ? Snaps.Near(_screens, before.Overlays[_overlay].Style.Orientation, _start.Size.Width, _start.Size.Height, at, Rows)
                    : null);
        if (drop.Result.Refused is not null || ConfigEdits.Replay(before, drop.Result.Config) is not { } change)
        {
            return;
        }

        // Undo takes back the very drop written, which a plain move takes from the cursor as the overlay does.
        var after = drop.Result.Config;
        if (drop.Kind == GestureKind.Move && after.Overlays[_overlay].Snap is null)
        {
            if (_settings.Release(press, Kept(cursor, at), distance, _overlay) is not { } released)
            {
                return;
            }

            after = released;
        }
        else
        {
            _settings.Edit(change);
        }

        undo?.Add(drop.Kind, before, after);
    }

    /// <summary>
    /// The place of the overlay shown under <paramref name="cursor"/>, <paramref name="self"/> apart, which a drop there
    /// merges into; null for none.
    /// </summary>
    public static int? Under(IReadOnlyList<OverlayWindow> overlays, int self, Position cursor)
    {
        for (var at = 0; at < overlays.Count; at++)
        {
            if (at != self && PInvoke.IsWindowVisible(overlays[at].Hwnd) && PInvoke.GetWindowRect(overlays[at].Hwnd, out var rect)
                && cursor.X >= rect.left && cursor.X < rect.right && cursor.Y >= rect.top && cursor.Y < rect.bottom)
            {
                return at;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _marks?.Dispose();
        if (!_ghost.IsNull)
        {
            PInvoke.DestroyWindow(_ghost);
        }
    }

    // A move dropped partly off its monitor's work area is written where the overlay then shows, inside it.
    private Position Kept(Position at) => Snaps.Inside(at, _screens, _start.Size.Width, _start.Size.Height);

    // The cursor as it would have been let go with the overlay where it is kept.
    private Position Kept(Position cursor, Position at)
    {
        var kept = Kept(at);
        return new(cursor.X + kept.X - at.X, cursor.Y + kept.Y - at.Y);
    }

    private Position PillAt(Position overlay) => new(overlay.X + (int)_start.PillBox.Left, overlay.Y + (int)_start.PillBox.Top);

    private void Carry(Position at)
    {
        if (_ghost.IsNull)
        {
            if (_start.PillPicture is not { } picture)
            {
                return;
            }

            _ghost = TopLevelWindow.Create(OverlayWindow.ClassName, OverlayWindow.ExStyle | WINDOW_EX_STYLE.WS_EX_TRANSPARENT, WINDOW_STYLE.WS_POPUP);
            using var bitmap = picture();
            OverlayWindow.Show(_ghost, bitmap, at);
            PInvoke.ShowWindow(_ghost, SHOW_WINDOW_CMD.SW_SHOWNA);
            return;
        }

        PInvoke.SetWindowPos(
            _ghost,
            HWND.Null,
            at.X,
            at.Y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
}
