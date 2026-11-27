using Thermi.Core.Messages;
using Thermi.Core.Ping;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// The ten minutes each item's chart shows in the window: the window's <see cref="Items"/>, which take the overlay's, sent
/// as the window opens, into the seconds they did not read themselves. Both are counted in one place, so the overlay's
/// seconds sit where they were once for all, whatever moment of a second the chart is drawn at. A second neither has stays
/// a gap.
/// </summary>
public sealed class ChartHistory(TimeProvider time)
{
    private static readonly int Slots = (int)SensorHistory.Kept.TotalSeconds;

    /// <summary>The window's own history of its items, by which they show what the overlay shows.</summary>
    public ItemHistory Items { get; } = new(time);

    /// <summary>Takes what the overlay sent, under what the window read itself.</summary>
    public void Take(History history) => Items.Take(history);

    /// <summary>
    /// Follows what each item of <paramref name="config"/> reads, as <see cref="ItemHistory.Follow"/> does: an item whose
    /// meter changed, by an edit or its undo, drops what it kept of the one before, the overlay's seconds too, and starts
    /// again from what <paramref name="read"/> gives of the new one, its seconds ending now, when it knows them.
    /// </summary>
    public void Follow(ThermiConfig config, Func<ItemConfig, IReadOnlyList<double?>?> read)
    {
        foreach (var item in Items.Follow(config.Overlays.SelectMany(overlay => overlay.Items), PingSensors.HostOf(config)))
        {
            if (read(item) is { } values)
            {
                Items.Refill(item.Id, values);
            }
        }
    }

    /// <summary>The item's ten minutes, a value a second ending now, oldest first, null for a gap.</summary>
    public IReadOnlyList<double?> Of(string id)
    {
        var own = Items.TenMinutes(id);
        return [.. Enumerable.Repeat<double?>(null, Slots - Math.Min(own.Count, Slots)), .. own.Skip(Math.Max(0, own.Count - Slots))];
    }
}
