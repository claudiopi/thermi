using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Editing;

/// <summary>
/// One change the window made, with what it found and what it left. Taking it back or making it again gives
/// null when the settings no longer hold what it expects there, as after a hand edit or a drag.
/// </summary>
internal interface IOperation
{
    /// <summary>What a step of this change alone is called.</summary>
    StepName Name { get; }

    /// <summary>True when it leaves what it changed as it found it.</summary>
    bool ChangesNothing => false;

    ThermiConfig? Undo(ThermiConfig config);

    ThermiConfig? Redo(ThermiConfig config);

    /// <summary>The same change, with a saved style renamed wherever it names it.</summary>
    IOperation Renamed(string from, string to) => this;

    /// <summary>This change and <paramref name="next"/> as one, when the next changes the same field again from what this left; else null.</summary>
    IOperation? Then(IOperation next) => null;
}

internal sealed record ThermiChange(Field<ThermiConfig> Field, object? Found, object? Wrote) : IOperation
{
    public StepName Name => StepNames.Of(Field.Name, Found, Wrote);

    public bool ChangesNothing => ConfigFields.Same(Found, Wrote);

    public ThermiConfig? Undo(ThermiConfig config) => Swap(config, Wrote, Found);

    public ThermiConfig? Redo(ThermiConfig config) => Swap(config, Found, Wrote);

    public IOperation? Then(IOperation next) =>
        next is ThermiChange change && change.Field == Field && ConfigFields.Same(change.Found, Wrote) ? this with { Wrote = change.Wrote } : null;

    private ThermiConfig? Swap(ThermiConfig config, object? from, object? to) =>
        ConfigFields.Same(Field.Get(config), from) ? Field.Set(config, to) : null;
}

internal sealed record OverlayChange(int Overlay, Field<OverlayConfig> Field, object? Found, object? Wrote) : IOperation
{
    public StepName Name => StepNames.Of(Field.Name, Found, Wrote);

    public bool ChangesNothing => ConfigFields.Same(Found, Wrote);

    public ThermiConfig? Undo(ThermiConfig config) => Swap(config, Wrote, Found);

    public ThermiConfig? Redo(ThermiConfig config) => Swap(config, Found, Wrote);

    public IOperation Renamed(string from, string to) =>
        Field == ConfigFields.StyleName ? this with { Found = Renamed(Found, from, to), Wrote = Renamed(Wrote, from, to) } : this;

    public IOperation? Then(IOperation next) =>
        next is OverlayChange change && change.Overlay == Overlay && change.Field == Field && ConfigFields.Same(change.Found, Wrote)
            ? this with { Wrote = change.Wrote }
            : null;

    private static object? Renamed(object? name, string from, string to) => SavedStyles.Same((string?)name, from) ? to : name;

    private ThermiConfig? Swap(ThermiConfig config, object? from, object? to) =>
        Overlay < config.Overlays.Count && ConfigFields.Same(Field.Get(config.Overlays[Overlay]), from)
            ? ConfigEdits.WithOverlay(config, Overlay, Field.Set(config.Overlays[Overlay], to))
            : null;
}

/// <summary>A field of an item changed; <see cref="Kind"/> is what the item reads, the unit its offset is named in.</summary>
internal sealed record ItemChange(string Id, Field<ItemConfig> Field, object? Found, object? Wrote, SensorKind? Kind = null) : IOperation
{
    /// <summary>The name of the metric the item was made from, which it shows while it stores no name of its own.</summary>
    public MetricName? Unnamed { get; init; }

    public StepName Name => Field == ConfigFields.ItemMetering
        ? StepNames.OfMetering((ConfigFields.Metering)Found!, (ConfigFields.Metering)Wrote!, Kind)
        : StepNames.Of(Field.Name, Shown(Found), Shown(Wrote), Kind);

    public bool ChangesNothing => ConfigFields.Same(Found, Wrote);

    public ThermiConfig? Undo(ThermiConfig config) => Swap(config, Wrote, Found);

    public ThermiConfig? Redo(ThermiConfig config) => Swap(config, Found, Wrote);

    private object? Shown(object? value) => value is null && Field.Name == ChangedField.ItemName ? Unnamed : value;

    public IOperation? Then(IOperation next) =>
        next is ItemChange change && change.Id == Id && change.Field == Field && ConfigFields.Same(change.Found, Wrote)
            ? this with { Wrote = change.Wrote }
            : null;

    private ThermiConfig? Swap(ThermiConfig config, object? from, object? to) =>
        ConfigEdits.Item(config, Id) is { } item && ConfigFields.Same(Field.Get(item), from)
            ? ConfigEdits.WithItem(config, Id, each => Field.Set(each, to))
            : null;
}

/// <summary>An item the window added, or deleted when not <see cref="Added"/>, at its place in an overlay.</summary>
internal sealed record ItemPresence(int Overlay, int Index, ItemConfig Item, bool Added) : IOperation
{
    public StepName Name => Added ? StepNames.Of(ChangedField.ItemAdded, null, NameOf(Item)) : StepNames.Of(ChangedField.ItemRemoved, NameOf(Item), null);

    public ThermiConfig? Undo(ThermiConfig config) => Added ? Remove(config) : Insert(config);

    public ThermiConfig? Redo(ThermiConfig config) => Added ? Insert(config) : Remove(config);

    private static object? NameOf(ItemConfig item) => item.Name ?? (object?)Metrics.Find(item.Metric ?? item.Auto)?.Name;

    private ThermiConfig? Remove(ThermiConfig config)
    {
        if (ConfigEdits.Find(config, Item.Id) is not var (overlay, index) || !ConfigFields.Same(config.Overlays[overlay].Items[index], Item))
        {
            return null;
        }

        return ConfigEdits.WithItems(config, overlay, [.. config.Overlays[overlay].Items.Where((_, at) => at != index)]);
    }

    // When its overlay is gone, its place is gone too, and it goes at the end of the last one.
    private ThermiConfig? Insert(ThermiConfig config)
    {
        if (ConfigEdits.Find(config, Item.Id) is not null)
        {
            return null;
        }

        var overlay = Math.Min(Overlay, config.Overlays.Count - 1);
        var items = config.Overlays[overlay].Items;
        var at = overlay == Overlay ? Math.Min(Index, items.Count) : items.Count;
        return ConfigEdits.WithItems(config, overlay, [.. items.Take(at), Item, .. items.Skip(at)]);
    }
}

/// <summary>An item the window moved within its overlay, from one place to another, each counted with the item in the list.</summary>
internal sealed record ItemMove(string Id, int Overlay, int From, int To) : IOperation
{
    public StepName Name => new(ChangedField.Place, null, null);

    public ThermiConfig? Undo(ThermiConfig config) => Move(config, To, From);

    public ThermiConfig? Redo(ThermiConfig config) => Move(config, From, To);

    private ThermiConfig? Move(ThermiConfig config, int from, int to)
    {
        if (Overlay >= config.Overlays.Count || from >= config.Overlays[Overlay].Items.Count || config.Overlays[Overlay].Items[from].Id != Id)
        {
            return null;
        }

        var items = config.Overlays[Overlay].Items.ToList();
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(Math.Min(to, items.Count), item);
        return ConfigEdits.WithItems(config, Overlay, items);
    }
}

/// <summary>An overlay the window added or duplicated, or deleted when not <see cref="Added"/>.</summary>
internal sealed record OverlayPresence(int Index, OverlayConfig Overlay, bool Added) : IOperation
{
    public StepName Name => Added ? new(ChangedField.OverlayAdded, null, Overlay.Name) : new(ChangedField.OverlayRemoved, Overlay.Name, null);

    public ThermiConfig? Undo(ThermiConfig config) => Added ? Remove(config) : Insert(config);

    public ThermiConfig? Redo(ThermiConfig config) => Added ? Insert(config) : Remove(config);

    public IOperation Renamed(string from, string to) => this with { Overlay = Overlay with { Style = SavedStyles.Renamed(Overlay.Style, from, to) } };

    private ThermiConfig? Remove(ThermiConfig config)
    {
        var index = config.Overlays.ToList().FindIndex(overlay => ConfigFields.Same(overlay, Overlay));
        return index < 0 ? null : config with { Overlays = [.. config.Overlays.Where((_, at) => at != index)] };
    }

    // An item of its own back in the settings means it was made again by hand, and two would share that id.
    private ThermiConfig? Insert(ThermiConfig config)
    {
        if (Overlay.Items.Any(item => ConfigEdits.Find(config, item.Id) is not null))
        {
            return null;
        }

        var at = Math.Min(Index, config.Overlays.Count);
        return config with { Overlays = [.. config.Overlays.Take(at), Overlay, .. config.Overlays.Skip(at)] };
    }
}
