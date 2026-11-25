using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Placement;

/// <summary>A desktop gesture's outcome: its kind, which names its undo, and its edit, refused as any edit can be.</summary>
public sealed record DesktopDrop(GestureKind Kind, EditResult Result);

/// <summary>
/// What a drag on an overlay does on the desktop, as one edit each: the overlay moves whole, onto a snap mark or into
/// another overlay, a pill held still and then dragged off joins another overlay or becomes one of its own, and a
/// floating overlay's corner resizes it.
/// </summary>
public static class DesktopGestures
{
    /// <summary>The steps a corner's drag changes the size by.</summary>
    public const double SizeStep = 0.05;

    /// <summary>How long a press holds still on a pill before the pill lifts, so a drag takes it off.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A drag let go with the pill or overlay it moved at <paramref name="dropped"/>, or on the <paramref name="mark"/> it
    /// was dropped on. The pill of <paramref name="takenOff"/>, an item whose pill the press <see cref="Lifts"/>, joins the
    /// overlay it was dropped <paramref name="onto"/> before the pill of the item <paramref name="before"/>, its own included,
    /// where it moves to that place, or becomes an overlay of its own where it was dropped; without one, the overlay merges
    /// into the overlay it was dropped onto at that place too, or goes where it was dropped. A pill held at the cap of
    /// overlays lifted nothing, so its drop is refused wherever it lands.
    /// </summary>
    public static DesktopDrop Release(ThermiConfig config, int overlay, string? takenOff, Position dropped, int? onto, SnapConfig? mark, string? before = null)
    {
        if (takenOff is not null && config.Overlays.Count >= ThermiConfig.MaxOverlays)
        {
            return new DesktopDrop(GestureKind.DragOff, ConfigEdits.Refuse(config, Refusal.TooManyOverlays));
        }

        if (takenOff is not null)
        {
            return onto is { } into
                ? new DesktopDrop(into == overlay ? GestureKind.Move : GestureKind.Merge, OverlayEdits.PillInto(config, takenOff, into, before))
                : new DesktopDrop(GestureKind.DragOff, OverlayEdits.DragOff(config, takenOff, dropped, mark));
        }

        return onto is { } target && target != overlay ? new DesktopDrop(GestureKind.Merge, OverlayEdits.Merge(config, overlay, target, before))
            : new DesktopDrop(GestureKind.Move, mark is { } snap ? OverlayEdits.Snap(config, overlay, snap) : OverlayEdits.Drop(config, overlay, dropped));
    }

    /// <summary>
    /// The overlay a drop joins, among <paramref name="shown"/> (null: not shown), <paramref name="self"/> apart: the one under
    /// the cursor, else the one the <paramref name="dragged"/> box covers most, by at least half of the smaller of the two.
    /// With it, the cursor brought onto it. None while <paramref name="free"/> (Alt held).
    /// </summary>
    public static (int Onto, Position At)? JoinTarget(IReadOnlyList<PixelRect?> shown, int self, PixelRect dragged, Position cursor, bool free = false)
    {
        if (free)
        {
            return null;
        }

        var others = shown.Select((box, at) => (Box: box.GetValueOrDefault(), At: at)).Where(each => each.At != self && each.Box.Area > 0).ToList();
        var under = others.Where(each => each.Box.Contains(cursor))
            .Concat(others.Where(each => Covers(each.Box, dragged)).OrderByDescending(each => each.Box.Shared(dragged)))
            .Select(each => ((PixelRect Box, int At)?)each)
            .FirstOrDefault();
        return under is var (box, onto)
            ? (onto, new Position(Math.Clamp(cursor.X, box.Left, box.Right - 1), Math.Clamp(cursor.Y, box.Top, box.Bottom - 1)))
            : null;
    }

    private static bool Covers(PixelRect one, PixelRect other) => one.Shared(other) * 2 >= Math.Min(one.Area, other.Area);

    /// <summary>
    /// The size and direction a floating overlay of <paramref name="width"/> by <paramref name="height"/> pixels at
    /// <paramref name="size"/> takes when its corner is dragged by <paramref name="dx"/> and <paramref name="dy"/>: vertical
    /// when the box dragged out is taller than wide, and grown as its longer side grew, in steps of 5 %, from 75 % to 200 %.
    /// </summary>
    public static (double Size, Orientation Orientation) Corner(double size, int width, int height, int dx, int dy)
    {
        var (wide, high) = (Math.Max(1, width + dx), Math.Max(1, height + dy));
        var grown = Math.Max(wide, high) / (double)Math.Max(1, Math.Max(width, height));
        var stepped = Math.Round(size * grown / SizeStep, MidpointRounding.AwayFromZero) * SizeStep;
        return (Math.Clamp(stepped, OverlayConfig.MinSize, OverlayConfig.MaxSize), high > wide ? Orientation.Vertical : Orientation.Horizontal);
    }

    /// <summary>The overlay's menu entry that takes the gesture back; an edit chosen from a menu is named by its entry instead.</summary>
    public static OverlayText UndoOf(GestureKind kind) => kind switch
    {
        GestureKind.Resize => OverlayText.UndoResize,
        GestureKind.DragOff => OverlayText.UndoDragOff,
        GestureKind.Merge => OverlayText.UndoMerge,
        GestureKind.Move or GestureKind.Nudge or GestureKind.Menu => OverlayText.UndoMove,
        _ => OverlayText.UndoMove,
    };

    /// <summary>
    /// Whether a press on that item's pill, or on the overlay's edge when none is named, held still for
    /// <paramref name="held"/>, takes hold of the pill instead of the whole overlay: only once it has held for
    /// <see cref="Hold"/>, since a plain drag is how a person moves an overlay, and never when the pill is the whole overlay.
    /// It then <see cref="Lifts"/> the pill, unless no other overlay fits: there the pill stays and its drag is refused,
    /// never turned into a move of the overlay the hold did not ask for.
    /// </summary>
    public static bool Holds(ThermiConfig config, int overlay, string? pill, TimeSpan held) =>
        held >= Hold && pill is not null && IsPart(config, overlay, pill);

    /// <summary>Whether a press held still for <paramref name="held"/> lifts the pill, so its drag takes the pill off its overlay.</summary>
    public static bool Lifts(ThermiConfig config, int overlay, string? pill, TimeSpan held) =>
        Holds(config, overlay, pill, held) && config.Overlays.Count < ThermiConfig.MaxOverlays;

    /// <summary>
    /// False when the pill is the whole overlay or no other overlay fits.
    /// </summary>
    public static bool CanTakeOff(ThermiConfig config, int overlay, string pill) =>
        config.Overlays.Count < ThermiConfig.MaxOverlays && IsPart(config, overlay, pill);

    /// <summary>Whether the pill is one of several of the overlay, which can come off it while another overlay fits.</summary>
    public static bool IsPart(ThermiConfig config, int overlay, string pill) =>
        ConfigEdits.Find(config, pill) is var (from, index)
        && !(from == overlay && ItemEdits.PillOf(config.Overlays[from], index) == (0, config.Overlays[from].Items.Count - 1));

    /// <summary>
    /// "Separate" from the overlay's menu: drops <paramref name="pill"/>, an overlay of the size <paramref name="made"/>
    /// once on its own, as a drag carries it, beside the overlay <paramref name="shown"/>, as Alt
    /// held would, so no mark pulls it back: the margin away, before a horizontal overlay and level with its middle, after it
    /// when that leaves the work area, below a vertical one and in line with its left edge, else above. Beside the overlay
    /// as it sits once the pill has left, <paramref name="rest"/> in size: one snapped by its right end, as beside the
    /// notification area, shrinks toward it. Refused as too many overlays where no other fits.
    /// </summary>
    public static DesktopDrop Separate(
        ThermiConfig config,
        int overlay,
        string pill,
        PixelRect shown,
        (int Width, int Height) made,
        (int Width, int Height) rest,
        IReadOnlyList<Screen> screens)
    {
        if (config.Overlays.Count >= ThermiConfig.MaxOverlays)
        {
            return new DesktopDrop(GestureKind.DragOff, ConfigEdits.Refuse(config, Refusal.TooManyOverlays));
        }

        if (!ConfigEdits.HasOverlay(config, overlay) || screens.Count == 0 || !IsPart(config, overlay, pill))
        {
            return new DesktopDrop(GestureKind.DragOff, ConfigEdits.Refuse(config, Refusal.NotFound));
        }

        var orientation = config.Overlays[overlay].Style.Orientation;
        if (Snaps.Locate(config.Overlays[overlay], screens, rest.Width, rest.Height) is { } then)
        {
            shown = orientation == Orientation.Vertical
                ? new PixelRect(shown.Left, then.Y, shown.Right, then.Y + rest.Height)
                : new PixelRect(then.X, shown.Top, then.X + rest.Width, shown.Bottom);
        }

        var screen = Snaps.ScreenOf(config.Overlays[overlay], screens)!;
        var gap = Snaps.Scaled(Snaps.Margin, screen);
        var (work, (width, height)) = (screen.Work, made);
        var at = orientation == Orientation.Vertical
            ? new Position(shown.Left, shown.Bottom + gap + height <= work.Bottom ? shown.Bottom + gap : shown.Top - gap - height)
            : new Position(shown.Left - gap - width >= work.Left ? shown.Left - gap - width : shown.Right + gap, shown.Top + ((shown.Height - height) / 2));
        var mark = Snaps.Near(screens, orientation, width, height, at, magnets: false);
        return Release(config, overlay, pill, Snaps.Inside(at, screens, width, height), onto: null, mark);
    }

    /// <summary>The ids of the items a drag of that item's pill takes off, in their order; none for an item the settings lack.</summary>
    public static IReadOnlyList<string> PillItems(ThermiConfig config, string pill)
    {
        if (ConfigEdits.Find(config, pill) is not var (from, index))
        {
            return [];
        }

        var (first, last) = ItemEdits.PillOf(config.Overlays[from], index);
        return [.. config.Overlays[from].Items.Skip(first).Take(last - first + 1).Select(item => item.Id)];
    }
}
