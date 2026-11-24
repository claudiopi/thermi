using Thermi.Core.Editing;
using Thermi.Core.Settings;

namespace Thermi.SettingsFile;

/// <summary>
/// The overlay's own changes that wait to be written to the settings file: each drag, by its overlay's first item, since
/// another process can delete or merge overlays before it is written, which moves the others' places; and each edit, as
/// a change of whatever the file holds then. Not thread-safe: <c>Settings</c> uses it under its lock.
/// </summary>
internal sealed class PendingWrites
{
    private readonly Dictionary<string, Position> _drops = new(StringComparer.Ordinal);
    private readonly List<Func<ThermiConfig, ThermiConfig>> _edits = [];

    public void Drop(string firstItem, Position at) => _drops[firstItem] = at;

    public void Add(Func<ThermiConfig, ThermiConfig> edit) => _edits.Add(edit);

    /// <summary>What waits now, copied, for a write that runs outside the lock.</summary>
    public Waiting Now() => new(new Dictionary<string, Position>(_drops, StringComparer.Ordinal), [.. _edits]);

    /// <summary>What waits, over <paramref name="config"/>.</summary>
    public ThermiConfig Over(ThermiConfig config) => Now().Over(config);

    // The edits written are the first that waited, since only a sync, one at a time, takes them out: counted, not
    // compared, as two presses of the same hotkey are equal delegates. A drag made again meanwhile stays.
    public void Written(Waiting written)
    {
        _edits.RemoveRange(0, written.Edits.Count);
        foreach (var (first, position) in written.Drops)
        {
            if (_drops.GetValueOrDefault(first) == position)
            {
                _drops.Remove(first);
            }
        }
    }

    /// <summary>The drags and edits that waited at one moment.</summary>
    public sealed record Waiting(IReadOnlyDictionary<string, Position> Drops, IReadOnlyList<Func<ThermiConfig, ThermiConfig>> Edits)
    {
        public bool Any => Drops.Count > 0 || Edits.Count > 0;

        // A drag of an overlay the file no longer has, or no longer starts with the same item, is dropped with it.
        public ThermiConfig Over(ThermiConfig config) => Edits.Aggregate(Drops.Aggregate(config, Drop), (edited, edit) => edit(edited));

        private static ThermiConfig Drop(ThermiConfig config, KeyValuePair<string, Position> drop)
        {
            var overlay = config.Overlays.ToList().FindIndex(each => each.Items is [var first, ..] && first.Id == drop.Key);
            return overlay < 0 ? config : OverlayEdits.Drop(config, overlay, drop.Value).Config;
        }
    }
}
