using System.Text.Json.Serialization;
using Thermi.Core.Search;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Core.Bridge;

/// <summary>
/// A sensor in the catalog: its id as an item names it, the name the window shows and where it comes from, its type, its
/// value now as text, or null with why it has none, its last minute since the window opened, a value a second, oldest
/// first, null for a gap, and for a name the library does not give or a chip only numbers, what its second line and its
/// tooltip say of it. With a search, what matched: the spans of its name, of its second line as its note or else its
/// type reads, and the metric it feeds that explains it where its name does not, with that name's spans, or null.
/// </summary>
public sealed record CatalogRow(
    string Id,
    string Name,
    NameFrom From,
    SensorKind Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] NoValue? Why,
    IReadOnlyList<double?> Minute,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Note,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Tip,
    IReadOnlyList<Mark> Marks,
    IReadOnlyList<Mark> LineMarks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Alias,
    IReadOnlyList<Mark> AliasMarks);

/// <summary>
/// A device of the catalog, by the library's id, with its name, the group it lists under, its sensors that match, and the
/// spans of its name a search's words start.
/// </summary>
public sealed record CatalogGroup(
    string Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DeviceKind? Kind,
    IReadOnlyList<CatalogRow> Sensors,
    IReadOnlyList<Mark> Marks);

/// <summary>
/// The catalog for a search: its devices, how many sensors they list, how many more it hides, and when it lists and hides
/// none, the one word to try instead.
/// </summary>
public sealed record CatalogView(
    IReadOnlyList<CatalogGroup> Groups,
    int Count,
    int Hidden,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Suggestion);

/// <summary>A pill of an overlay: its label and its items' ids, in order.</summary>
public sealed record PillView(string Label, IReadOnlyList<string> Items);

/// <summary>
/// An item as the overlay shows it: its value as text, the overlay's own text with its units, or null while it reads
/// nothing; its level, or null while lines cannot judge it or it reads nothing; the type of its sensor; the sensor its
/// metric picks; what its Warn by dropdown offers, its own reading first, then its other readings alone; and the name of
/// what it warns by, as "CPU fan duty" or a sensor's own, or null while it warns by its own reading.
/// </summary>
public sealed record ItemView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Level? Level,
    string Type,
    string? Auto,
    IReadOnlyList<WarnByChoice> WarnBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WarnByName)
{
    /// <summary>Whether lines judge what it warns by: it then has lines, a graph, only on alert and the notice.</summary>
    public bool Judged { get; init; }

    /// <summary>Whether it can show its speed in bits per second: a network's, as its plans are sold, never a drive's.</summary>
    public bool Bits { get; init; }

    /// <summary>How many of its bars' segments the overlay lights, as a look's sample shows them, or null where it draws none.</summary>
    public int? Lit { get; init; }

    /// <summary>Its offset with the unit it is typed in, as "2 °C", or null for a type that takes none.</summary>
    public string? OffsetShown { get; init; }

    /// <summary>What its Sensor dropdown lists: its own device's sensors of its type, then, asked to show all, the others'.</summary>
    public SensorOffer Offer { get; init; } = SensorOffer.None;

    /// <summary>What its value is when not the value now, as "avg", said beside it; null for the value now.</summary>
    public string? Tag { get; init; }

    /// <summary>The label the overlay draws for the run of labels the item is in, its own when it starts one; null for none.</summary>
    public string? Label { get; init; }
}

/// <summary>A reading an item can warn by, null for its own, with the name its Warn by dropdown shows.</summary>
public sealed record WarnByChoice(WarnByConfig? WarnBy, string Name);

/// <summary>Every overlay's pills, every item by its id, every sensor's value as text, by the sensor's id, and thermi's mark as the tray shows it.</summary>
public sealed record WindowView(
    IReadOnlyList<IReadOnlyList<PillView>> Pills,
    IReadOnlyDictionary<string, ItemView> Items,
    IReadOnlyDictionary<string, string> Values,
    MarkState Mark);

/// <summary>
/// An overlay, or a look's sample, painted as the overlay paints it: a PNG as a data URL, its size in its own pixels, which
/// the page shows one to each of its device pixels, so it is never resampled, and the color of the taskbar it sits on as
/// #RRGGBB, or null where it sits on none.
/// </summary>
public sealed record OverlayPicture(
    string Png,
    double Width,
    double Height,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Taskbar);

/// <summary>Each look's tile sample: Default's, and each saved look's by its name.</summary>
public sealed record LookSamples(OverlayPicture Default, IReadOnlyDictionary<string, OverlayPicture> Saved);

/// <summary>Why a chart has no readings to draw: the service stopped, a CPU sensor needs thermi's driver, or none yet.</summary>
public enum ChartEmpty
{
    Stopped,
    Driver,
    None,
}

/// <summary>Where the chart's line takes its level's color, in the meter's unit, the level holding up to the next stop.</summary>
public sealed record ColorStop(double At, Level Level);

/// <summary>
/// An item's chart: its track, from 0 to <see cref="Max"/> by <see cref="Step"/>, the span it shows, its ten minutes from
/// its first reading, a second each, and each as the item's value says it, its lines and their texts, the colors along it,
/// its Min · Avg · Max and its summary for Narrator, why it is empty, the message of a line pushed by the other, its title,
/// the ends of its span as its scale says them, when its lines are not the defaults and the lowest was reached for most of
/// the ten minutes, what the button that puts the defaults back says, as "Use 90/95 °C", else null; and how long ago its
/// first reading came, as "4 min ago".
/// </summary>
public sealed record ChartView(
    double Max,
    double Step,
    double From,
    double To,
    IReadOnlyList<double?> Readings,
    IReadOnlyList<string?> ReadingTexts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Warning,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] double? Critical,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? WarningText,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CriticalText,
    IReadOnlyList<ColorStop> Colors,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Stats,
    string Summary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ChartEmpty? Empty,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Pushed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Title,
    string FromText,
    string ToText,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Fires,
    string StartText);
