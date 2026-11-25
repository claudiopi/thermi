using System.Globalization;
using Thermi.Core.Translations;

namespace Thermi.Core.Sensors;

/// <summary>What an input of a board's Super I/O chip likely reads, where the chip's usual wiring says it.</summary>
[Translated]
public enum LikelyName
{
    System,
    Chipset,
    Cpu,
    PcieSlot,
    Vrm,
    CpuFan,
    SystemFan1,
    SystemFan2,
}

/// <summary>Where the name the window shows for a sensor comes from: the library, a probable name, or the user.</summary>
public enum NameFrom
{
    Library,
    Likely,
    Own,
}

/// <summary>
/// The name the window shows for a sensor, where it comes from, and the library's own; for an input a Super I/O chip names
/// only by its number, the chip and that number too.
/// </summary>
public sealed record SensorName(string Shown, NameFrom From, string Library, string? Chip = null, int? Input = null);

/// <summary>The names the window shows for sensors: the one the user gave, else a probable one, else the library's.</summary>
public static class SensorNames
{
    public const int MaxLength = 40;

    // The library names a chip's inputs by their place in its own wiring table of boards, and by number on a board it does
    // not know. On these ITE chips, which only Gigabyte uses, that table wires the first inputs alike on nearly every board.
    private static readonly string[] GigabyteChips = ["IT8686E", "IT8688E", "IT8689E", "IT8696E"];

    private static readonly Dictionary<(SensorKind Kind, int Input), LikelyName> GigabyteWiring = new()
    {
        [(SensorKind.Temperature, 1)] = LikelyName.System,
        [(SensorKind.Temperature, 2)] = LikelyName.Chipset,
        [(SensorKind.Temperature, 3)] = LikelyName.Cpu,
        [(SensorKind.Temperature, 4)] = LikelyName.PcieSlot,
        [(SensorKind.Temperature, 5)] = LikelyName.Vrm,
        [(SensorKind.Fan, 1)] = LikelyName.CpuFan,
        [(SensorKind.Fan, 2)] = LikelyName.SystemFan1,
        [(SensorKind.Fan, 3)] = LikelyName.SystemFan2,
    };

    private static readonly string[] Numbered = ["Temperature #", "Fan #"];

    /// <summary>
    /// The name of <paramref name="sensor"/>, which an item names <paramref name="id"/>: the one <paramref name="own"/>
    /// gives it, else its probable name in the language of <paramref name="texts"/>, else the library's.
    /// </summary>
    public static SensorName Of(RawSensor sensor, string id, IReadOnlyDictionary<string, string>? own = null, Texts? texts = null)
    {
        var input = InputOf(sensor);
        var chip = input is null ? null : sensor.Hardware;
        return own?.GetValueOrDefault(id) is { } name ? new SensorName(name, NameFrom.Own, sensor.Name, chip, input)
            : input is { } number && Likely(sensor, number) is { } likely
                ? new SensorName(LikelyOf(sensor, likely, texts ?? Texts.English), NameFrom.Likely, sensor.Name, chip, number)
            : new SensorName(DutyOf(sensor, sensor.Name, texts ?? Texts.English), NameFrom.Library, sensor.Name, chip, input);
    }

    /// <summary>The probable name of a board's input, or null where the chip's usual wiring does not say it.</summary>
    public static LikelyName? LikelyOf(RawSensor sensor) => InputOf(sensor) is { } input ? Likely(sensor, input) : null;

    /// <summary>
    /// What the window writes under a name it did not take from the library: "Likely" with the library's for a probable
    /// one, the library's for the user's, and the chip and its input for an input the chip only numbers; else null.
    /// </summary>
    public static string? NoteOf(SensorName name, Texts texts, IFormatProvider numbers) => name switch
    {
        { From: NameFrom.Likely } => texts.Format(numbers, CatalogText.LikelyNote, name.Library),
        { From: NameFrom.Own } => name.Library,
        { Chip: { } chip, Input: { } input } => texts.Format(numbers, CatalogText.InputNote, chip, input),
        _ => null,
    };

    // The library names a fan's duty as the fan, so its name says it is the duty, never reading as the fan itself; its
    // "GPU Fan" then reads "GPU fan duty", in thermi's case, as the CPU fan's does.
    private static string LikelyOf(RawSensor sensor, LikelyName likely, Texts texts) => DutyOf(sensor, texts.Of(likely), texts);

    // A fan reads "GPU fan" too, as thermi writes the CPU fan.
    private static string DutyOf(RawSensor sensor, string name, Texts texts)
    {
        var kind = SensorMap.KindOf(sensor);
        var fan = kind is SensorKind.Control or SensorKind.Fan ? name.Replace(" Fan", " fan", StringComparison.Ordinal) : name;
        return kind == SensorKind.Control && !name.Contains("Control", StringComparison.OrdinalIgnoreCase)
            ? texts.Format(CultureInfo.InvariantCulture, CatalogText.LikelyDuty, fan) : fan;
    }

    /// <summary>The snapshot's sensors as the window names them everywhere: the user's name, else the probable one, else the library's.</summary>
    public static IEnumerable<RawSensor> Named(Snapshot snapshot, IReadOnlyDictionary<string, string>? own, Texts texts) =>
        All(snapshot, own, texts).Select(named => named.Sensor with { Name = named.Name.Shown });

    /// <summary>
    /// Every sensor of the snapshot with the id an item names it by and its name as <see cref="Of"/> gives it, but that a
    /// library's name that a sensor of another type of the same device also has says its type, as "GPU Core (load)" beside
    /// "GPU Core (temperature)", so a name never reads as another kind's.
    /// </summary>
    public static IReadOnlyList<(string Id, RawSensor Sensor, SensorName Name)> All(Snapshot snapshot, IReadOnlyDictionary<string, string>? own, Texts? texts)
    {
        var language = texts ?? Texts.English;
        var named = SensorMap.Named(snapshot).Select(each => (each.Id, each.Sensor, Name: Of(each.Sensor, each.Id, own, language))).ToList();
        var shared = named
            .Where(each => each.Name.From == NameFrom.Library && SensorMap.KindOf(each.Sensor) is not null)
            .GroupBy(each => (Device: SensorMap.DeviceIdOf(each.Sensor), each.Name.Shown))
            .Where(same => same.Select(each => SensorMap.KindOf(each.Sensor)).Distinct().Count() > 1)
            .Select(same => same.Key)
            .ToHashSet();
        return
        [
            .. named.Select(each => shared.Contains((SensorMap.DeviceIdOf(each.Sensor), each.Name.Shown))
                && each.Name.From == NameFrom.Library
                && SensorMap.KindOf(each.Sensor) is { } kind
                ? (each.Id, each.Sensor, each.Name with { Shown = WithKind(each.Name.Shown, kind, language) })
                : each),
        ];
    }

    private static string WithKind(string name, SensorKind kind, Texts texts) =>
        texts.Format(CultureInfo.InvariantCulture, CatalogText.WithKind, name, texts.Of(kind).ToLowerInvariant());

    // A duty names its fan's input as the fan does.
    private static int? InputOf(RawSensor sensor) =>
        sensor.HardwareType == "SuperIO" && Array.Find(Numbered, prefix => sensor.Name.StartsWith(prefix, StringComparison.Ordinal)) is { } prefix
            && int.TryParse(sensor.Name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var input)
            ? input
            : null;

    private static LikelyName? Likely(RawSensor sensor, int input) =>
        Array.Exists(GigabyteChips, chip => sensor.Hardware.EndsWith(chip, StringComparison.Ordinal))
            && SensorMap.KindOf(sensor) is { } kind
            && GigabyteWiring.TryGetValue((kind == SensorKind.Control ? SensorKind.Fan : kind, input), out var name)
            ? name
            : null;
}
