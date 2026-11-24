using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Sent = Thermi.Core.Messages.ItemHistory;

namespace Thermi.Core.Look;

/// <summary>
/// The ten minutes each item's chart shows in the window: the overlay's, sent once as the window opens and moved on by
/// the time gone since on the clock both processes share, under what the window itself read since it opened. A second
/// neither has stays a gap.
/// </summary>
public sealed class ChartHistory(TimeProvider time)
{
    private static readonly int Slots = (int)SensorHistory.Kept.TotalSeconds;

    private readonly Dictionary<string, Sent> _sent = new(StringComparer.Ordinal);

    /// <summary>Keeps what the overlay sent, over anything it sent before.</summary>
    public void Take(History history)
    {
        foreach (var item in history.Items)
        {
            _sent[item.ItemId] = item;
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
