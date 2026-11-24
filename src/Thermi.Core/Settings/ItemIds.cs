using System.Globalization;

namespace Thermi.Core.Settings;

/// <summary>
/// Gives every item an id unique in the whole file and the same on every read of it. Every id already in
/// the file is reserved first, and the first item holding one keeps it; any other item gets its metric or
/// sensor id, or that id with the first free suffix, as in "cpu.temperature-2".
/// </summary>
public static class ItemIds
{
    public static ThermiConfig Assign(ThermiConfig config)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var keeps = new Queue<bool>(config.Overlays.SelectMany(overlay => overlay.Items).Select(item => item.Id is { Length: > 0 } id && taken.Add(id)));
        var overlays = new List<OverlayConfig>(config.Overlays.Count);
        foreach (var overlay in config.Overlays)
        {
            var items = new List<ItemConfig>(overlay.Items.Count);
            foreach (var item in overlay.Items)
            {
                items.Add(keeps.Dequeue() ? item : item with { Id = Free(Base(item), taken) });
            }

            overlays.Add(overlay with { Items = items });
        }

        return config with { Overlays = overlays };
    }

    private static string Base(ItemConfig item) => item.Metric ?? item.Auto ?? item.Sensor ?? (item.Sensors is [var first, ..] ? first : null) ?? "item";

    private static string Free(string id, HashSet<string> taken)
    {
        var candidate = id;
        var suffix = 1;
        while (!taken.Add(candidate))
        {
            suffix++;
            candidate = string.Create(CultureInfo.InvariantCulture, $"{id}-{suffix}");
        }

        return candidate;
    }
}
