using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

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
            ConfigEdits.Apply(
                config,
                current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Snap = null, Position = position })));

    /// <summary>
    /// A press on the overlay let go at <paramref name="released"/>: past the drag distance, the overlay is dropped where the
    /// cursor took it, off its snap. A click, or any press on a locked overlay, gives null: nothing changes, so nothing is written.
    /// </summary>
    public static EditResult? Release(ThermiConfig config, int overlay, Press press, Position released, DragDistance distance) =>
        !ConfigEdits.HasOverlay(config, overlay) ? ConfigEdits.Refuse(config, Refusal.NotFound)
        : Snaps.Dragged(press, released, distance, config.Overlays[overlay].Locked) is { } dropped ? Drop(config, overlay, dropped)
        : null;

    /// <summary>
    /// A new overlay of the default items at the first free corner, its items in the order the first overlay gives them.
    /// It has no name of its own, so it is named from where it sits (<see cref="Names.OfOverlays"/>).
    /// </summary>
    public static EditResult Add(ThermiConfig config) =>
        config.Overlays.Count >= ThermiConfig.MaxOverlays
            ? ConfigEdits.Refuse(config, Refusal.TooManyOverlays)
            : ConfigEdits.Apply(config, current => current with
            {
                Overlays =
                [
                    .. current.Overlays,
                    Cornered(current, new OverlayConfig { Items = InOrderOf(OverlayConfig.DefaultItems, current.Overlays[0].Items) }),
                ],
            });

    /// <summary>A copy of an overlay right after it, at the first free corner, its items with ids of their own; a name the user typed is not copied.</summary>
    public static EditResult Duplicate(ThermiConfig config, int overlay) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
        {
            if (config.Overlays.Count >= ThermiConfig.MaxOverlays)
            {
                return ConfigEdits.Refuse(config, Refusal.TooManyOverlays);
            }

            var copy = Cornered(
                config,
                config.Overlays[overlay] with { Name = null, Items = [.. config.Overlays[overlay].Items.Select(item => item with { Id = "" })] });
            return ConfigEdits.Apply(
                config,
                current => current with { Overlays = [.. current.Overlays.Take(overlay + 1), copy, .. current.Overlays.Skip(overlay + 1)] });
        });

    public static EditResult Delete(ThermiConfig config, int overlay) =>
        ConfigEdits.OnOverlay(config, overlay, () => config.Overlays.Count == 1
            ? ConfigEdits.Refuse(config, Refusal.LastOverlay)
            : ConfigEdits.Apply(config, current => current with { Overlays = [.. current.Overlays.Where((_, at) => at != overlay)] }));

    /// <summary>One default overlay, shown always. What belongs to thermi stays: where it shows, the hotkeys, the language and the temperature unit.</summary>
    public static EditResult Reset(ThermiConfig config) =>
        ConfigEdits.Apply(config, current => current with { Overlays = [new OverlayConfig()] });

    /// <summary>Gives the overlay a name of its own, which moving it keeps.</summary>
    public static EditResult Rename(ThermiConfig config, int overlay, string name) =>
        ConfigEdits.OnOverlay(config, overlay, () => name.Trim() is { Length: > 0 } trimmed
            ? ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Name = trimmed }))
            : ConfigEdits.Refuse(config, Refusal.NameEmpty));

    /// <summary>Hides the overlay on its own, which is saved, or shows it again.</summary>
    public static EditResult SetHidden(ThermiConfig config, int overlay, bool hidden) =>
        ConfigEdits.OnOverlay(config, overlay, () =>
            ConfigEdits.Apply(config, current => ConfigEdits.WithOverlay(current, overlay, current.Overlays[overlay] with { Hidden = hidden })));

    /// <summary>
    /// Moves every item of <paramref name="overlay"/> into <paramref name="into"/>, before the pill of the item
    /// <paramref name="before"/> or at its end, and removes it.
    /// </summary>
    public static EditResult Merge(ThermiConfig config, int overlay, int into, string? before = null)
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
        var merged = Joined(target, Labeled(config.Overlays[overlay].Items), before);
        return ConfigEdits.Apply(config, current => current with
        {
            Overlays = [.. current.Overlays.Select((each, at) => at == into ? merged : each).Where((_, at) => at != overlay)],
        });
    }

    /// <summary>
    /// Moves an item to the end of another overlay, or of a new one when <paramref name="overlay"/> is null;
    /// an overlay it leaves empty goes.
    /// </summary>
    public static EditResult MoveItem(ThermiConfig config, string id, int? overlay)
    {
        if (ConfigEdits.Find(config, id) is not var (from, index) || (overlay is { } to && !ConfigEdits.HasOverlay(config, to)))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        if (overlay is null && config.Overlays.Count >= ThermiConfig.MaxOverlays)
        {
            return ConfigEdits.Refuse(config, Refusal.TooManyOverlays);
        }

        return overlay == from ? new EditResult(config) : Split(config, from, index, index, new Landing(overlay));
    }

    /// <summary>
    /// A pill dragged off its overlay: its items become an overlay of their own where it was dropped, or at the
    /// <paramref name="snap"/> it was dropped onto. The pill that is the whole overlay moves it there instead.
    /// </summary>
    public static EditResult DragOff(ThermiConfig config, string id, Position at, SnapConfig? snap = null)
    {
        if (ConfigEdits.Find(config, id) is not var (from, index))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var (first, last) = ItemEdits.PillOf(config.Overlays[from], index);
        if (first == 0 && last == config.Overlays[from].Items.Count - 1)
        {
            return snap is null ? Drop(config, from, at) : Snap(config, from, snap);
        }

        return config.Overlays.Count >= ThermiConfig.MaxOverlays
            ? ConfigEdits.Refuse(config, Refusal.TooManyOverlays)
            : Split(config, from, first, last, new Landing(Dropped: at, SnapTo: snap));
    }

    /// <summary>
    /// A pill dragged off its overlay onto an overlay: its items join that one where it was dropped, before the pill of the
    /// item <paramref name="before"/>, or at its end when none is named. Dropped back on its own overlay it moves there,
    /// and dropped on itself it stays.
    /// </summary>
    public static EditResult PillInto(ThermiConfig config, string id, int into, string? before = null)
    {
        if (ConfigEdits.Find(config, id) is not var (from, index) || !ConfigEdits.HasOverlay(config, into))
        {
            return ConfigEdits.Refuse(config, Refusal.NotFound);
        }

        var (first, last) = ItemEdits.PillOf(config.Overlays[from], index);
        var onItself = from == into && config.Overlays[from].Items.Skip(first).Take(last - first + 1).Any(item => item.Id == before);
        return onItself ? new EditResult(config) : Split(config, from, first, last, new Landing(into, before));
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

    /// <summary>
    /// Alt+arrows on a floating overlay's row: it moves by the pixels given; on the taskbar, left and right move it along
    /// the taskbar from where it was dropped. A snapped overlay stays on its snap, and the window says why, since where it
    /// shows is the overlay's to work out.
    /// </summary>
    public static EditResult Nudge(ThermiConfig config, int overlay, int dx, int dy) =>
        ConfigEdits.OnOverlay(config, overlay, () => config.Overlays[overlay] switch
        {
            { Snap: null, Position: { } position } => Drop(config, overlay, new Position(position.X + dx, position.Y + dy)),
            { Snap: { Spot: SnapSpot.Taskbar, Offset: { } offset } snap } when dy == 0 =>
                Snap(config, overlay, snap with { Offset = Math.Max(0, offset + dx) }),
            _ => ConfigEdits.Refuse(config, Refusal.Snapped),
        });

    // Where a pill's items go: into another overlay, before the pill of an item of it or at its end, or to a new one, at the
    // snap or the place it was dropped, or else at a free corner.
    private readonly record struct Landing(int? Into = null, string? Before = null, Position? Dropped = null, SnapConfig? SnapTo = null);

    // Items first to last of one overlay go into another or to a new one, with the look and size they had; the overlay they
    // leave empty goes.
    private static EditResult Split(ThermiConfig config, int from, int first, int last, Landing landing)
    {
        var source = config.Overlays[from];
        var items = Labeled(source.Items);
        var moved = items.Skip(first).Take(last - first + 1).ToList();
        var left = items.Where((_, at) => at < first || at > last).ToList();
        var overlays = config.Overlays.Select((each, at) => at == from ? each with { Items = left } : each).ToList();
        if (landing.Into is { } target)
        {
            overlays[target] = Joined(overlays[target], moved, landing.Before);
        }
        else
        {
            var made = new OverlayConfig(source.Size)
            {
                Style = source.Style,
                Items = moved,
                Position = landing.SnapTo is null ? landing.Dropped : null,
                Snap = landing.SnapTo,
            };
            overlays.Add(landing.Dropped is null && landing.SnapTo is null ? Cornered(config, made) : made);
        }

        return ConfigEdits.Apply(config, current => current with { Overlays = [.. overlays.Where(each => each.Items.Count > 0)] });
    }

    // Before a pill, never inside one, and its labels written, so every pill of the overlay shows as it did.
    private static OverlayConfig Joined(OverlayConfig overlay, List<ItemConfig> moved, string? before)
    {
        var found = before is null ? -1 : overlay.Items.ToList().FindIndex(item => item.Id == before);
        if (found < 0)
        {
            return overlay with { Items = [.. overlay.Items, .. moved] };
        }

        var at = ItemEdits.PillOf(overlay, found).First;
        var items = Labeled(overlay.Items);
        return overlay with { Items = [.. items.Take(at), .. moved, .. items.Skip(at)] };
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

    private static OverlayConfig Cornered(ThermiConfig config, OverlayConfig overlay) =>
        overlay with { Snap = new SnapConfig(Names.FreeCorner(config)), Position = null };

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
