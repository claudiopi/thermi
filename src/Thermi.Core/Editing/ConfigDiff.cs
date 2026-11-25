using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Editing;

/// <summary>
/// Turns what an edit changed into operations that, applied in order, lead from the settings before it to
/// the settings after it, each one valid at its turn: overlays deleted, items deleted, overlays added, then
/// fields, moves and added items. Taking them back in reverse order leads back.
/// </summary>
internal static class ConfigDiff
{
    public static IReadOnlyList<IOperation> Between(ThermiConfig before, ThermiConfig after)
    {
        var operations = new List<IOperation>();
        operations.AddRange(Changes(ConfigFields.Thermi, before, after).Select(change => new ThermiChange(change.Field, change.Found, change.Wrote)));
        var pairs = Match(before.Overlays, after.Overlays);
        var kept = pairs.Select(pair => pair.Before).ToHashSet();
        for (var index = before.Overlays.Count - 1; index >= 0; index--)
        {
            if (!kept.Contains(index))
            {
                operations.Add(new OverlayPresence(index, before.Overlays[index], Added: false));
            }
        }

        // Until the new overlays come, the overlays that stay are all there is, so each sits at its rank among them.
        for (var rank = 0; rank < pairs.Count; rank++)
        {
            operations.AddRange(Deleted(rank, before.Overlays[pairs[rank].Before], after.Overlays[pairs[rank].After]));
        }

        var matched = pairs.Select(pair => pair.After).ToHashSet();
        operations.AddRange(Enumerable.Range(0, after.Overlays.Count)
            .Where(index => !matched.Contains(index))
            .Select(index => new OverlayPresence(index, after.Overlays[index], Added: true)));
        foreach (var (was, now) in pairs)
        {
            var (first, second) = (before.Overlays[was], after.Overlays[now]);
            operations.AddRange(Changes(ConfigFields.Overlay, first, second)
                .Select(change => new OverlayChange(now, change.Field, change.Found, change.Wrote)));
            operations.AddRange(ItemChanges(first, second));
            operations.AddRange(Placed(now, first, second));
        }

        return operations;
    }

    private static IEnumerable<(Field<T> Field, object? Found, object? Wrote)> Changes<T>(IReadOnlyList<Field<T>> fields, T before, T after) =>
        fields.Select(field => (field, field.Get(before), field.Get(after))).Where(change => !ConfigFields.Same(change.Item2, change.Item3));

    // Overlays have no id: one that keeps any item is the same overlay, and the others pair up in order
    // between those, so a reset changes the first overlay field by field instead of replacing it.
    private static List<(int Before, int After)> Match(IReadOnlyList<OverlayConfig> before, IReadOnlyList<OverlayConfig> after)
    {
        var pairs = new List<(int Before, int After)>();
        var (was, now) = (0, 0);
        foreach (var (sharedBefore, sharedAfter) in SharingItems(before, after).Append((before.Count, after.Count)))
        {
            for (; was < sharedBefore && now < sharedAfter; was++, now++)
            {
                pairs.Add((was, now));
            }

            if (sharedBefore < before.Count)
            {
                pairs.Add((sharedBefore, sharedAfter));
            }

            (was, now) = (sharedBefore + 1, sharedAfter + 1);
        }

        return pairs;
    }

    private static List<(int Before, int After)> SharingItems(IReadOnlyList<OverlayConfig> before, IReadOnlyList<OverlayConfig> after)
    {
        var pairs = new List<(int Before, int After)>();
        var next = 0;
        for (var now = 0; now < after.Count; now++)
        {
            var ids = Ids(after[now]);
            var (best, most) = (-1, 0);
            for (var was = next; was < before.Count; was++)
            {
                var shared = before[was].Items.Count(item => ids.Contains(item.Id));
                (best, most) = shared > most ? (was, shared) : (best, most);
            }

            if (best >= 0)
            {
                pairs.Add((best, now));
                next = best + 1;
            }
        }

        return pairs;
    }

    // From the last, so the places of the others still hold as each goes.
    private static IEnumerable<IOperation> Deleted(int overlay, OverlayConfig before, OverlayConfig after)
    {
        var kept = Ids(after);
        for (var index = before.Items.Count - 1; index >= 0; index--)
        {
            if (!kept.Contains(before.Items[index].Id))
            {
                yield return new ItemPresence(overlay, index, before.Items[index], Added: false);
            }
        }
    }

    private static IEnumerable<IOperation> ItemChanges(OverlayConfig before, OverlayConfig after)
    {
        var was = before.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        return after.Items.Where(item => was.ContainsKey(item.Id)).SelectMany(item =>
            Changes(ConfigFields.Item, was[item.Id], item)
                .Select(change => new ItemChange(item.Id, change.Field, change.Found, change.Wrote, Threshold.KindOf(item))
                {
                    Unnamed = Metrics.Find(was[item.Id].Metric ?? was[item.Id].Auto)?.Name,
                }));
    }

    // The longest run of items that kept their order stays put; each other item moves, or is added, right
    // after the item it follows in the new order, which is already in its place by then.
    private static IEnumerable<IOperation> Placed(int overlay, OverlayConfig before, OverlayConfig after)
    {
        var target = after.Items;
        var common = Ids(before);
        common.IntersectWith(Ids(after));
        var current = before.Items.Select(item => item.Id).Where(common.Contains).ToList();
        var stays = LongestCommonSubsequence(current, [.. target.Select(item => item.Id).Where(common.Contains)]);
        for (var index = 0; index < target.Count; index++)
        {
            var item = target[index];
            if (stays.Contains(item.Id))
            {
                continue;
            }

            var from = current.IndexOf(item.Id);
            if (from >= 0)
            {
                current.RemoveAt(from);
            }

            var to = index == 0 ? 0 : current.IndexOf(target[index - 1].Id) + 1;
            current.Insert(to, item.Id);
            yield return from >= 0 ? new ItemMove(item.Id, overlay, from, to) : new ItemPresence(overlay, to, item, Added: true);
        }
    }

    private static HashSet<string> LongestCommonSubsequence(List<string> first, List<string> second)
    {
        var lengths = new int[first.Count + 1, second.Count + 1];
        for (var i = first.Count - 1; i >= 0; i--)
        {
            for (var j = second.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = first[i] == second[j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var stays = new HashSet<string>(StringComparer.Ordinal);
        for (var (i, j) = (0, 0); i < first.Count && j < second.Count;)
        {
            if (first[i] == second[j])
            {
                stays.Add(first[i]);
                (i, j) = (i + 1, j + 1);
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return stays;
    }

    private static HashSet<string> Ids(OverlayConfig overlay) => overlay.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
}
