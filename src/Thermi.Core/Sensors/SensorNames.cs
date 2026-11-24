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
            : input is { } number && Likely(sensor, number) is { } likely ? new SensorName(LikelyOf(sensor, likely, texts ?? Texts.English), NameFrom.Likely, sensor.Name, chip, number)
            : new SensorName(sensor.Name, NameFrom.Library, sensor.Name, chip, input);
    }

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

    // The library names a fan's duty as the fan, so its probable name says it is the duty.
    private static string LikelyOf(RawSensor sensor, LikelyName likely, Texts texts) =>
        SensorMap.KindOf(sensor) == SensorKind.Control
            ? texts.Format(CultureInfo.InvariantCulture, CatalogText.LikelyDuty, texts.Of(likely))
            : texts.Of(likely);

    /// <summary>The snapshot's sensors with the names the user gave them, as the window names them.</summary>
    public static IEnumerable<RawSensor> Named(Snapshot snapshot, IReadOnlyDictionary<string, string>? own) =>
        SensorMap.Named(snapshot).Select(named => own?.GetValueOrDefault(named.Id) is { } name ? named.Sensor with { Name = name } : named.Sensor);

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
