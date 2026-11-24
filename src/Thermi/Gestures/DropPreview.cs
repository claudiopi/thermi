namespace Thermi.Gestures;

/// <summary>
/// The overlay a drag is over now, and the item before whose pill a drop there joins it, null past its last: shared by
/// every overlay, so the one under the drag marks where it joins, and kept clear of overlays the drag is not over.
/// </summary>
internal sealed class DropPreview
{
    public int? Onto { get; private set; }

    public string? Before { get; private set; }

    /// <summary>The drag is now over <paramref name="target"/>; true when that changed what an overlay marks.</summary>
    public bool Over(DropTarget target)
    {
        var before = target.Onto is null ? null : target.Before;
        if (target.Onto == Onto && before == Before)
        {
            return false;
        }

        (Onto, Before) = (target.Onto, before);
        return true;
    }
}
