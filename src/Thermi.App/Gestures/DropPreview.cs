using Thermi.Core.Placement;

namespace Thermi.App.Gestures;

/// <summary>
/// The overlay a drag is over now, and the item before whose pill a drop there joins it, null past its last, or, for an
/// item carried over its own pill, before which item of that pill it goes: shared by every overlay, so the one under the
/// drag marks where it joins, and kept clear of overlays the drag is not over.
/// </summary>
internal sealed class DropPreview
{
    public int? Onto { get; private set; }

    public string? Before { get; private set; }

    public bool InPill { get; private set; }

    /// <summary>The drag is now over <paramref name="target"/>; true when that changed what an overlay marks.</summary>
    public bool Over(DropTarget target)
    {
        var (before, inPill) = target.Onto is null ? (null, false) : (target.Before, target.InPill);
        if (target.Onto == Onto && before == Before && inPill == InPill)
        {
            return false;
        }

        (Onto, Before, InPill) = (target.Onto, before, inPill);
        return true;
    }
}
