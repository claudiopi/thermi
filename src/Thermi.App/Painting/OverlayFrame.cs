using Thermi.Core.Look;
using Thermi.Core.Settings;

namespace Thermi.App.Painting;

/// <summary>
/// What one paint of an overlay shows, by what draws it. A layered window keeps its picture until it is given another,
/// so a paint that would show what the last one showed is left out: at rest most seconds bring the same numbers, and each
/// paint costs the overlay its drawing and the desktop's compositor a new picture.
/// </summary>
internal sealed record OverlayFrame(IReadOnlyList<OverlayItem> Items, OverlayLook Look, float Scale, Position At)
{
    /// <summary>The overlay's settings, which say what else it draws, as the corner that resizes it.</summary>
    public OverlayConfig? Overlay { get; init; }

    /// <summary>The text of the "Sensors stopped" pill while it shows, null while the sensors run.</summary>
    public string? Stopped { get; init; }

    /// <summary>What the open window marks: the item selected, the one pointed at, and the accent they are marked in.</summary>
    public (string? Selected, string? Pointed, Rgb Accent) Marks { get; init; }

    /// <summary>The opacity it shows at, 255 for whole.</summary>
    public byte Alpha { get; init; } = 255;

    /// <summary>Whether a glow or a drag moves something on it, so each of its frames is new.</summary>
    public bool Moving { get; init; }

    /// <summary>
    /// Whether this frame needs a paint after <paramref name="last"/>, the last one painted or null before any: unless
    /// both show the same, it does. A frame after a moving one paints too, so the last of a glow or a drag never stays.
    /// </summary>
    public bool Differs(OverlayFrame? last) =>
        last is null
        || Moving
        || last.Moving
        || (Scale, Alpha, Stopped, Marks) != (last.Scale, last.Alpha, last.Stopped, last.Marks)
        || At != last.At
        || Look != last.Look
        || !Equals(Overlay, last.Overlay)
        || Items.Count != last.Items.Count
        || Items.Zip(last.Items).Any(pair => !Same(pair.First, pair.Second));

    // An item's line is a list, which its record compares by reference; every other part of it compares by value, a part
    // added later included.
    private static bool Same(OverlayItem item, OverlayItem other) =>
        item with { Graph = null } == other with { Graph = null }
        && (item.Graph is null ? other.Graph is null : other.Graph is not null && item.Graph.SequenceEqual(other.Graph));
}
