using System.Text.Json.Serialization;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Core.Bridge;

/// <summary>
/// A sensor in the catalog: its id as an item names it, the name the window shows and where it comes from, its type, its
/// value now as text, or null with why it has none, its last minute since the window opened, a value a second, oldest
/// first, null for a gap, and for a name the library does not give or a chip only numbers, what its second line and its
/// tooltip say of it.
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Tip);

/// <summary>A device of the catalog, by the library's id, with its name, the group it lists under, and its sensors that match.</summary>
public sealed record CatalogGroup(
    string Id,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DeviceKind? Kind,
    IReadOnlyList<CatalogRow> Sensors);

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
/// An item as the overlay shows it: its value as text, or null while it reads nothing; its meter's level, or null with no
/// meter or no reading; the type of its sensor; the sensor its metric picks; the meters it is offered, readings of the
/// item itself alone; and its meter's name, as "CPU fan duty" or a sensor's own, or null without a meter.
/// </summary>
public sealed record ItemView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Level? Level,
    string Type,
    string? Auto,
    IReadOnlyList<MeterChoice> Meters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? MeterName)
{
    /// <summary>Whether it can show its speed in bits per second: a network's, as its plans are sold, never a drive's.</summary>
    public bool Bits { get; init; }

    /// <summary>Its offset with the unit it is typed in, as "2 °C", or null for a type that takes none.</summary>
    public string? OffsetShown { get; init; }
}

/// <summary>A meter an item is offered, with the name its Meter dropdown shows.</summary>
public sealed record MeterChoice(MeterConfig Meter, string Name);

/// <summary>Every overlay's pills, every item by its id, every sensor's value as text, by the sensor's id, and thermi's mark as the tray shows it.</summary>
public sealed record WindowView(
    IReadOnlyList<IReadOnlyList<PillView>> Pills,
    IReadOnlyDictionary<string, ItemView> Items,
    IReadOnlyDictionary<string, string> Values,
    MarkState Mark);

/// <summary>Why a chart has no readings to draw: the service stopped, a CPU sensor needs thermi's driver, or none yet.</summary>
public enum ChartEmpty
{
    Stopped,
    Driver,
    None,
}

/// <summary>Where the chart's line takes a color, in the meter's unit.</summary>
public sealed record ColorStop(double At, ChartColor Color);

/// <summary>
/// An item's chart: its track, from 0 to <see cref="Max"/> by <see cref="Step"/>, the span it shows, its ten minutes, its
/// lines and their texts, the colors along it, its Min · Avg · Max and its summary for Narrator, why it is empty, the
/// message of a line pushed by the other, its title, the ends of its span as its scale says them, and, when its lines are
/// not the defaults and the lowest was reached for most of the ten minutes, what the button that puts the defaults back
/// says, as "Use 90/95 °C"; else null.
/// </summary>
public sealed record ChartView(
    double Max,
    double Step,
    double From,
    double To,
    IReadOnlyList<double?> Readings,
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Fires);
