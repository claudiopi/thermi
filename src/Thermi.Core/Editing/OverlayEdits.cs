using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Editing;

/// <summary>The window's edits of whole overlays, each found by its place in the file, and the reset.</summary>
public static class OverlayEdits
{
    /// <summary>Sets the size, a factor from 75 % to 200 % over the monitor's own scale.</summary>
    public static EditResult SetSize(ThermiConfig config, int overlay, double size) =>
        ConfigEdits.OnOverlay(config, overlay, () => size is >= OverlayConfig.MinSize and <= OverlayConfig.MaxSize
            ? ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Size = size }))
            : ConfigEdits.Refuse(config, Refusal.SizeOutOfRange));

    /// <summary>Lets every click on the overlay pass to what is below, or catches them again.</summary>
    public static EditResult SetClickThrough(ThermiConfig config, int overlay, bool on) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { ClickThrough = on })));

    /// <summary>Keeps a drag from moving the overlay, or lets it move again; clicks work either way.</summary>
    public static EditResult SetLocked(ThermiConfig config, int overlay, bool on) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Locked = on })));

    /// <summary>Moves the overlay 1 px every 3 minutes around a small square, so an OLED screen never keeps its picture, or keeps it still.</summary>
    public static EditResult SetOled(ThermiConfig config, int overlay, bool on) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Oled = on })));

    /// <summary>Fades the overlay as the pointer comes near it, so what lies under it shows while it still takes clicks.</summary>
    public static EditResult SetFade(ThermiConfig config, int overlay, bool on) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Fade = on })));

    /// <summary>Shows the overlay always, hides it while a full-screen app covers its monitor, or shows it only then.</summary>
    public static EditResult SetVisibility(ThermiConfig config, int overlay, Visibility visibility) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Visibility = visibility })));

    /// <summary>Snaps the overlay to a spot, which leaves its free position out of the file.</summary>
    public static EditResult Snap(ThermiConfig config, int overlay, SnapConfig snap) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Snap = snap, Position = null })));

    /// <summary>Leaves the overlay where a drag dropped it, which takes it off its snap.</summary>
    public static EditResult Drop(ThermiConfig config, int overlay, Position position) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Snap = null, Position = position })));

    /// <summary>
    /// A press on the overlay let go at <paramref name="released"/>: past the drag distance, the overlay is dropped where the
    /// cursor took it, off its snap. A click, or any press on a locked overlay, gives null: nothing changes, so nothing is written.
    /// </summary>
    public static EditResult? Release(ThermiConfig config, int overlay, Press press, Position released, DragDistance distance) =>
        !ConfigEdits.HasOverlay(config, overlay) ? ConfigEdits.Refuse(config, Refusal.NotFound)
        : Snaps.Dragged(press, released, distance, config.Overlays[overlay].Locked) is { } dropped ? Drop(config, overlay, dropped)
        : null;

    /// <summary>A new overlay of the default items, named in English; the window names its own in its language.</summary>
    public static EditResult Add(ThermiConfig config) => Add(config, Texts.English);

    /// <summary>
    /// A new overlay of the default items at the first free corner, named in <paramref name="texts"/>' language from where it
    /// sits, its items in the order the first overlay gives them.
    /// </summary>
    public static EditResult Add(ThermiConfig config, Texts texts) =>
        config.Overlays.Count >= ThermiConfig.MaxOverlays
            ? ConfigEdits.Refuse(config, Refusal.TooManyOverlays)
            : ConfigEdits.Apply(config, current => current with
            {
                Overlays = [.. current.Overlays, Cornered(current, new OverlayConfig { Items = InOrderOf(OverlayConfig.DefaultItems, current.Overlays[0].Items) }, texts)],
            });

    /// <summary>A copy of an overlay right after it, named in English; the window names its own in its language.</summary>
    public static EditResult Duplicate(ThermiConfig config, int overlay) => Duplicate(config, overlay, Texts.English);

    /// <summary>A copy of an overlay right after it, at the first free corner, named from there, its items with ids of their own.</summary>
    public static EditResult Duplicate(ThermiConfig config, int overlay, Texts texts) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
        {
            if (config.Overlays.Count >= ThermiConfig.MaxOverlays)
            {
                return ConfigEdits.Refuse(config, Refusal.TooManyOverlays);
            }

            var copy = Cornered(config, config.Overlays[overlay] with { Items = [.. config.Overlays[overlay].Items.Select(item => item with { Id = "" })] }, texts);
            return ConfigEdits.Apply(config, current => current with { Overlays = [.. current.Overlays.Take(overlay + 1), copy, .. current.Overlays.Skip(overlay + 1)] });
        });

    public static EditResult Delete(ThermiConfig config, int overlay) =>
        ConfigEdits.OnOverlay(config, overlay, () => config.Overlays.Count == 1
            ? ConfigEdits.Refuse(config, Refusal.LastOverlay)
            : ConfigEdits.Apply(config, current => current with { Overlays = [.. current.Overlays.Where((_, at) => at != overlay)] }));

    /// <summary>One default overlay, shown always. What belongs to thermi stays: where it shows, the hotkeys, the language and the temperature unit.</summary>
    public static EditResult Reset(ThermiConfig config) =>
        ConfigEdits.Apply(config, current => current with { Overlays = [new OverlayConfig()] });

    /// <summary>Gives the overlay a name of its own; moving it never does.</summary>
    public static EditResult Rename(ThermiConfig config, int overlay, string name) =>
        ConfigEdits.OnOverlay(config, overlay, () => name.Trim() is { Length: > 0 } trimmed
            ? ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Name = trimmed }))
            : ConfigEdits.Refuse(config, Refusal.NameEmpty));

    /// <summary>Hides the overlay on its own, which is saved, or shows it again.</summary>
    public static EditResult SetHidden(ThermiConfig config, int overlay, bool hidden) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Hidden = hidden })));

    /// <summary>Moves every item of <paramref name="overlay"/> to the end of <paramref name="into"/>, and removes it.</summary>
    public static EditResult Merge(ThermiConfig config, int overlay, int into)
    {
        if (!ConfigEdits.HasOverlay(config, overlay) || !ConfigEdits.HasOverlay(config, into))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (overlay == into)
        {
            return new EditResult(config);
        }

        var target = config.Overlays[into];
        var merged = target with { Items = [.. target.Items, .. Labeled(config.Overlays[overlay].Items)] };
        return ConfigEdits.Apply(config, current => current with
        {
            Overlays = [.. current.Overlays.Select((each, at) => at == into ? merged : each).Where((_, at) => at != overlay)],
        });
    }

    /// <summary>Moves an item to the end of another overlay, or of a new one when <paramref name="overlay"/> is null; an overlay it leaves empty goes.</summary>
    public static EditResult MoveItem(ThermiConfig config, string id, int? overlay, Texts texts)
    {
        if (ConfigEdits.Find(config, id) is not var (from, index) || (overlay is { } to && !ConfigEdits.HasOverlay(config, to)))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (overlay is null && config.Overlays.Count >= ThermiConfig.MaxOverlays)
        {
            return ConfigEdits.Refuse(config, Refusal.TooManyOverlays);
        }

        return overlay == from ? new EditResult(config) : Split(config, from, index, index, overlay, null, texts);
    }

    /// <summary>
    /// A pill dragged off its overlay: its items become an overlay of their own where it was dropped. The pill that is the
    /// whole overlay moves it there instead.
    /// </summary>
    public static EditResult DragOff(ThermiConfig config, string id, Position at, Texts texts)
    {
        if (ConfigEdits.Find(config, id) is not var (from, index))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var (first, last) = ItemEdits.PillOf(config.Overlays[from], index);
        if (first == 0 && last == config.Overlays[from].Items.Count - 1)
        {
            return Drop(config, from, at);
        }

        return config.Overlays.Count >= ThermiConfig.MaxOverlays
            ? ConfigEdits.Refuse(config, Refusal.TooManyOverlays)
            : Split(config, from, first, last, null, at, texts);
    }

    /// <summary>A pill dragged off its overlay onto another: its items go to the end of that one, as a merge does.</summary>
    public static EditResult PillInto(ThermiConfig config, string id, int into, Texts texts)
    {
        if (ConfigEdits.Find(config, id) is not var (from, index) || !ConfigEdits.HasOverlay(config, into))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var (first, last) = ItemEdits.PillOf(config.Overlays[from], index);
        return from == into ? new EditResult(config) : Split(config, from, first, last, into, null, texts);
    }

    /// <summary>A floating overlay's corner dragged: its size, and the direction the drag's shape gave.</summary>
    public static EditResult Resize(ThermiConfig config, int overlay, double size, Orientation orientation) =>
        ConfigEdits.OnOverlay(config, overlay, () => size is >= OverlayConfig.MinSize and <= OverlayConfig.MaxSize
            ? ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with
            {
                Size = size,
                Style = current.Overlays[overlay].Style with { Orientation = orientation },
            }))
            : ConfigEdits.Refuse(config, Refusal.SizeOutOfRange));

    /// <summary>Alt+arrows on a floating overlay's row: it moves by the pixels given. A snapped overlay stays on its snap.</summary>
    public static EditResult Nudge(ThermiConfig config, int overlay, int dx, int dy) =>
        ConfigEdits.OnOverlay(config, overlay, () => config.Overlays[overlay].Position is { } position && config.Overlays[overlay].Snap is null
            ? Drop(config, overlay, new Position(position.X + dx, position.Y + dy))
            : new EditResult(config));

    // Items first to last of one overlay go to the end of another, or to a new one, at a free corner or where it was dropped,
    // with the look and size they had; the overlay they leave empty goes.
    private static EditResult Split(ThermiConfig config, int from, int first, int last, int? into, Position? dropped, Texts texts)
    {
        var source = config.Overlays[from];
        var items = Labeled(source.Items);
        var moved = items.Skip(first).Take(last - first + 1).ToList();
        var left = items.Where((_, at) => at < first || at > last).ToList();
        var overlays = config.Overlays.Select((each, at) => at == from ? each with { Items = left } : each).ToList();
        if (into is { } target)
        {
            overlays[target] = overlays[target] with { Items = [.. overlays[target].Items, .. moved] };
        }
        else
        {
            var made = new OverlayConfig(source.Size) { Style = source.Style, Items = moved, Position = dropped };
            overlays.Add(dropped is null ? Cornered(config, made, texts) : made with { Name = Names.OfOverlay(config, made, texts) });
        }

        return ConfigEdits.Apply(config, current => current with { Overlays = [.. overlays.Where(each => each.Items.Count > 0)] });
    }

    // The items that also show in the order's metrics take, among their own places, the order those give them; the rest
    // keep their places.
    private static List<ItemConfig> InOrderOf(IReadOnlyList<ItemConfig> items, IReadOnlyList<ItemConfig> order)
    {
        var at = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (metric, index) in order.Select((item, index) => (item.Metric, index)))
        {
            if (metric is not null)
            {
                at.TryAdd(metric, index);
            }
        }

        var sorted = new Queue<ItemConfig>(items.Where(item => item.Metric is { } metric && at.ContainsKey(metric)).OrderBy(item => at[item.Metric!]));
        return [.. items.Select(item => item.Metric is { } metric && at.ContainsKey(metric) ? sorted.Dequeue() : item)];
    }

    private static OverlayConfig Cornered(ThermiConfig config, OverlayConfig overlay, Texts texts)
    {
        var placed = overlay with { Snap = new SnapConfig(Names.FreeCorner(config)), Position = null };
        return placed with { Name = Names.OfOverlay(config, placed, texts) };
    }

    // An item that showed the label of the run before it gets that label written, so a move never changes what it shows.
    private static List<ItemConfig> Labeled(IReadOnlyList<ItemConfig> items)
    {
        var labels = OverlayLayout.RunLabels(items);
        return [.. items.Select((item, at) => OverlayLayout.LabelOf(item) is null && labels[at] is { } label ? item with { Label = label } : item)];
    }

    /// <summary>
    /// Every overlay takes the saved style after the first overlay's, in name order: the first one when the
    /// first overlay's style is not a saved one. Without saved styles, nothing changes.
    /// </summary>
    public static ThermiConfig NextStyle(ThermiConfig config, IEnumerable<StyleConfig> saved)
    {
        var styles = saved.Where(style => style.Name is not null).OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (styles.Count == 0)
        {
            return config;
        }

        var current = styles.FindIndex(style => string.Equals(style.Name, config.Overlays[0].Style.Name, StringComparison.OrdinalIgnoreCase));
        var next = styles[(current + 1) % styles.Count];
        return config with { Overlays = [.. config.Overlays.Select(overlay => overlay with { Style = next.WornOn(overlay.Style) })] };
    }
}
