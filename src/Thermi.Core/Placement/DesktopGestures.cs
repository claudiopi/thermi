using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Placement;

/// <summary>A desktop gesture's outcome: its kind, which names its undo, and its edit, refused as any edit can be.</summary>
public sealed record DesktopDrop(GestureKind Kind, EditResult Result);

/// <summary>What a held press takes: the pill of <see cref="Item"/>, or, <see cref="Alone"/>, that item off its pill.</summary>
public sealed record Carried(string Item, bool Alone = false);

/// <summary>
/// Where a drag is let go: the overlay it joins, if any, before the pill of the item <see cref="Before"/>, or past its last
/// pill for none; or, <see cref="InPill"/>, an item carried alone over its own pill, before that pill's item
/// <see cref="Before"/>, or past the pill's last for none.
/// </summary>
public readonly record struct DropTarget(int? Onto, string? Before, bool InPill = false);

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
    /// was dropped on. What the press <see cref="Lifts"/>, <paramref name="carried"/>, joins the overlay it was dropped onto
    /// where the <paramref name="target"/> says, its own included, where it moves to that place, or becomes an overlay of its
    /// own where it was dropped; an item alone dropped on its own pill moves within it. Without one, the overlay merges into
    /// the overlay it was dropped onto at that place too, or goes where it was dropped. A hold at the cap of overlays lifted
    /// nothing, so its drop is refused wherever it lands.
    /// </summary>
    public static DesktopDrop Release(ThermiConfig config, int overlay, Carried? carried, Position dropped, DropTarget target, SnapConfig? mark)
    {
        if (carried is not null && config.Overlays.Count >= ThermiConfig.MaxOverlays)
        {
            return new DesktopDrop(GestureKind.DragOff, ConfigEdits.Refuse(config, Refusal.TooManyOverlays));
        }

        if (carried is { } off)
        {
            return target.Onto is { } into
                ? new DesktopDrop(
                    into == overlay ? GestureKind.Move : GestureKind.Merge,
                    off.Alone ? OverlayEdits.ItemInto(config, off.Item, into, target) : OverlayEdits.PillInto(config, off.Item, into, target.Before))
                : new DesktopDrop(GestureKind.DragOff, OverlayEdits.DragOff(config, off.Item, dropped, mark, off.Alone));
        }

        return target.Onto is { } onto && onto != overlay ? new DesktopDrop(GestureKind.Merge, OverlayEdits.Merge(config, overlay, onto, target.Before))
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
    /// when the box dragged out is taller than wide, and grown as its longer side grew, in steps of 5 %, from 75 % to 400 %.
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
    /// What a press on <paramref name="item"/> takes once held, null on the overlay's edge: the item alone when pressed off
    /// its pill's label, in a pill of several items that shows one, so a value moves on its own; its pill otherwise, as
    /// a press on the label, or anywhere on a pill of one item or without a label, takes.
    /// </summary>
    public static Carried? Grip(ThermiConfig config, string? item, bool onLabel) =>
        item is null ? null
        : new Carried(item, Alone: !onLabel && ConfigEdits.Find(config, item) is var (from, index)
            && !config.Overlays[from].Style.NoLabels
            && ItemEdits.PillOf(config.Overlays[from], index) is var (first, last) && last > first
            && OverlayLayout.RunLabels(config.Overlays[from].Items)[first] is not null);

    /// <summary>
    /// Whether a press that took <paramref name="carried"/>, or the overlay's edge for none, held still for
    /// <paramref name="held"/>, takes hold of it instead of the whole overlay: only once it has held for
    /// <see cref="Hold"/>, since a plain drag is how a person moves an overlay, never when the pill is the whole overlay,
    /// and never on a locked overlay. It then <see cref="Lifts"/> it, unless no other overlay fits: there it stays and
    /// its drag is refused, never turned into a move of the overlay the hold did not ask for.
    /// </summary>
    public static bool Holds(ThermiConfig config, int overlay, Carried? carried, TimeSpan held) =>
        held >= Hold && carried is not null && ConfigEdits.HasOverlay(config, overlay) && !config.Overlays[overlay].Locked
        && (carried.Alone || IsPart(config, overlay, carried.Item));

    /// <summary>Whether a press held still for <paramref name="held"/> lifts what it took, so its drag takes it off its overlay.</summary>
    public static bool Lifts(ThermiConfig config, int overlay, Carried? carried, TimeSpan held) =>
        Holds(config, overlay, carried, held) && config.Overlays.Count < ThermiConfig.MaxOverlays;

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
        return Release(config, overlay, new Carried(pill), Snaps.Inside(at, screens, width, height), default, mark);
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
