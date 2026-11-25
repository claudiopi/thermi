using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Look;

/// <summary>
/// The ten minutes each item's chart shows in the window: the overlay's, sent once as the window opens and moved on by
/// the time gone since on the clock both processes share, under what the window itself read since it opened. A second
/// neither has stays a gap. It holds the window's <see cref="Items"/>, which take the rest of what the overlay sends.
/// </summary>
public sealed class ChartHistory(TimeProvider time)
{
    private static readonly int Slots = (int)SensorHistory.Kept.TotalSeconds;

    private readonly Dictionary<string, Messages.ItemHistory> _sent = new(StringComparer.Ordinal);

    /// <summary>The window's own history of its items, by which they show what the overlay shows.</summary>
    public ItemHistory Items { get; } = new(time);

    /// <summary>Keeps what the overlay sent, over anything it sent before.</summary>
    public void Take(History history)
    {
        foreach (var item in history.Items)
        {
            _sent[item.ItemId] = item;
        }

        Items.Take(history);
    }

    /// <summary>
    /// Follows what each item of <paramref name="config"/> reads, as <see cref="ItemHistory.Follow"/> does: an item whose
    /// meter changed, by an edit or its undo, drops what the overlay sent of the one before too, and starts again from
    /// what <paramref name="read"/> gives of the new one, its seconds ending now, when it knows them.
    /// </summary>
    public void Follow(ThermiConfig config, Func<ItemConfig, IReadOnlyList<double?>?> read)
    {
        foreach (var item in Items.Follow(config.Overlays.SelectMany(overlay => overlay.Items)))
        {
            _sent.Remove(item.Id);
            if (read(item) is { } values)
            {
                Items.Refill(item.Id, values);
            }
        }
    }

    /// <summary>
    /// The item's ten minutes, a value a second ending now, oldest first, null for a gap: <paramref name="own"/>, the
    /// window's own readings ending now, wins wherever it has a value.
    /// </summary>
    public IReadOnlyList<double?> Of(string id, IReadOnlyList<double?> own)
    {
        var values = new double?[Slots];
        if (_sent.TryGetValue(id, out var sent))
        {
            var behind = (int)Math.Round(time.GetElapsedTime(sent.Newest).TotalSeconds);
            for (var at = 0; at < Slots; at++)
            {
                var from = sent.Slots.Count - Slots + at + behind;
                values[at] = from >= 0 && from < sent.Slots.Count && float.IsFinite(sent.Slots[from]) ? sent.Slots[from] : null;
            }
        }

        var start = Slots - Math.Min(own.Count, Slots);
        for (var at = start; at < Slots; at++)
        {
            values[at] = own[own.Count - Slots + at] ?? values[at];
        }

        return values;
    }
}
