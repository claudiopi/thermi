using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Placement;

/// <summary>A desktop gesture's outcome: its kind, which names its undo, and its edit, refused as any edit can be.</summary>
public sealed record DesktopDrop(GestureKind Kind, EditResult Result);

/// <summary>
/// What a drag on an overlay does on the desktop, as one edit each: the overlay moves whole, onto a snap mark or into
/// another overlay, a pill dragged off with Ctrl becomes an overlay of its own, and a floating overlay's corner resizes it.
/// </summary>
public static class DesktopGestures
{
    /// <summary>The steps a corner's drag changes the size by.</summary>
    public const double SizeStep = 0.05;

    /// <summary>
    /// A drag let go with the pill or overlay it moved at <paramref name="dropped"/>. The pill of <paramref name="takenOff"/>,
    /// an item whose pill the drag <see cref="TakesOff"/>, joins the overlay it was dropped <paramref name="onto"/>, or
    /// becomes an overlay of its own there; without one, the overlay merges into the overlay it was dropped onto, snaps to
    /// the <paramref name="mark"/> it was dropped on, or stays where it was dropped.
    /// </summary>
    public static DesktopDrop Release(ThermiConfig config, int overlay, string? takenOff, Position dropped, int? onto, SnapConfig? mark, Texts texts)
    {
        var other = onto is { } under && under != overlay ? onto : null;
        if (takenOff is not null)
        {
            return other is { } into
                ? new DesktopDrop(GestureKind.Merge, OverlayEdits.PillInto(config, takenOff, into, texts))
                : new DesktopDrop(GestureKind.DragOff, OverlayEdits.DragOff(config, takenOff, dropped, texts));
        }

        return other is { } target ? new DesktopDrop(GestureKind.Merge, OverlayEdits.Merge(config, overlay, target))
            : new DesktopDrop(GestureKind.Move, mark is { } snap ? OverlayEdits.Snap(config, overlay, snap) : OverlayEdits.Drop(config, overlay, dropped));
    }

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

    /// <summary>The overlay's menu entry that takes the gesture back.</summary>
    public static OverlayText UndoOf(GestureKind kind) => kind switch
    {
        GestureKind.Resize => OverlayText.UndoResize,
        GestureKind.DragOff => OverlayText.UndoDragOff,
        GestureKind.Merge => OverlayText.UndoMerge,
        GestureKind.Move or GestureKind.Nudge => OverlayText.UndoMove,
        _ => OverlayText.UndoMove,
    };

    /// <summary>
    /// Whether a drag of that item's pill, or of the overlay's edge when none is named, takes the pill off its overlay
    /// instead of moving the whole overlay: only with Ctrl held, since a plain drag is how a person moves an overlay, and
    /// never when the pill is the whole overlay or there is no room for another overlay, where a drag that would be refused
    /// at its drop moves the overlay instead.
    /// </summary>
    public static bool TakesOff(ThermiConfig config, int overlay, string? pill, bool ctrl) =>
        ctrl && pill is not null && config.Overlays.Count < ThermiConfig.MaxOverlays && ConfigEdits.Find(config, pill) is var (from, index)
        && !(from == overlay && ItemEdits.PillOf(config.Overlays[from], index) == (0, config.Overlays[from].Items.Count - 1));

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
