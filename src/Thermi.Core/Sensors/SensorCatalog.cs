using System.Globalization;
using Thermi.Core.Translations;

namespace Thermi.Core.Sensors;

/// <summary>
/// The words, each separated by a space, that find a type of sensor or a kind of device in the catalog's search beside its
/// own name, in each language: a member per <see cref="SensorKind"/> and per <see cref="DeviceKind"/>, by the same name.
/// </summary>
[Translated]
public enum Synonyms
{
    Temperature,
    Load,
    Clock,
    Power,
    Fan,
    Control,
    Data,
    Throughput,
    Duration,
    Time,
    Address,
    Cpu,
    Gpu,
    Ram,
    Board,
    Ssd,
    Network,
    Battery,
    System,
}

/// <summary>Why a sensor has no usable value. The window says each in words of its own from the translations.</summary>
[Translated]
public enum NoValue
{
    /// <summary>The sensor reports nothing, or not a number.</summary>
    ReadsNothing,

    /// <summary>A CPU temperature, power or clock of exactly zero: the PawnIO driver may be missing.</summary>
    DriverMayBeMissing,

    /// <summary>A fan at 0 RPM: stopped or not connected. Its zero is real, so it still shows.</summary>
    Stopped,
}

/// <summary>
/// One row of the catalog. <see cref="Id"/> is what an item names it by, which for a repeated id is not
/// <see cref="RawSensor.Id"/> (see <see cref="SensorMap.Named"/>). <see cref="Reading"/> follows the overlay's zero
/// rule, <see cref="Why"/> says why it has no usable value, or is null when it has one, and <see cref="Name"/> is the
/// name the window shows for it.
/// </summary>
public sealed record CatalogSensor(string Id, RawSensor Sensor, Reading Reading, NoValue? Why, SensorName Name);

/// <summary>
/// One device and its sensors. <see cref="Id"/> is the library's id of the device, so two of the same model stay apart.
/// <see cref="Kind"/> is the group the window lists it under, as Network, Disk or Battery; null for one of no known kind.
/// </summary>
public sealed record CatalogDevice(string Id, string Name, IReadOnlyList<CatalogSensor> Sensors, DeviceKind? Kind = null);

/// <summary>Every sensor the service reads, grouped by device, and the search and type filter over them.</summary>
public static class SensorCatalog
{
    private const string D3dLayer = "D3D ";

    // A CPU's clock as the time it ran, beside the cores' own, and its bus's, which never moves.
    private const string Effective = "Effective";
    private const string BusSpeed = "Bus Speed";

    private const string Storage = "Storage";
    private const string Level = "Level";
    private const string MemoryModule = "/dimm/";

    // An NVMe drive's thresholds, which the library lists as sensors of their own.
    private static readonly string[] DriveLimits = ["Warning Temperature", "Critical Temperature"];

    // A memory module's thermal limits, its sensor's resolution and its size, which its chip reports and never change.
    private static readonly string[] ModuleConstants = ["Limit", "Resolution", "Capacity"];

    private static readonly char[] NotLetters = [' ', '(', ')', '#', '/', '-', ',', '.', ':'];

    /// <summary>
    /// The devices in the order the service reports them, each with its sensors by type, in the library's order within one
    /// type, named as <see cref="SensorNames.Of"/> names them with the user's names <paramref name="own"/>.
    /// </summary>
    public static IReadOnlyList<CatalogDevice> Build(Snapshot snapshot, IReadOnlyDictionary<string, string>? own = null, Texts? texts = null) =>
    [
        .. SensorNames.All(snapshot, own, texts)
            .Select(named => Row(named.Id, named.Sensor, named.Name))
            .OfType<CatalogSensor>()
            .GroupBy(row => SensorMap.DeviceIdOf(row.Sensor), StringComparer.Ordinal)
            .Select(device => new CatalogDevice(
                device.Key,
                device.First().Sensor.Hardware,
                [.. device.OrderBy(row => row.Reading.Kind)],
                SensorMap.DeviceOf(device.First().Sensor))),
    ];

    /// <summary>
    /// The sensors whose name, the library's or the one shown, its note, device or type has a word starting with every word of
    /// <paramref name="search"/>, ignoring case, and whose type is
    /// one of <paramref name="kinds"/>, or any type when there are none. A device without such a sensor is left out. A type
    /// matches by the library's name and by its name in the language of <paramref name="texts"/>, and a word also matches as
    /// the start of a common synonym of the type or the kind of device, in that language or in English, as "heat" or "ssd".
    /// </summary>
    public static IReadOnlyList<CatalogDevice> Filter(
        IReadOnlyList<CatalogDevice> devices,
        string search,
        IReadOnlyCollection<SensorKind> kinds,
        Texts? texts = null)
    {
        var language = texts ?? Texts.English;
        var words = WordsOf(search);
        return
        [
            .. devices
                .Select(device => device with
                {
                    Sensors =
                    [
                        .. device.Sensors.Where(row =>
                            (kinds.Count == 0 || kinds.Contains(row.Reading.Kind)) && words.All(word => Matches(device, row, word, language))),
                    ],
                })
                .Where(device => device.Sensors.Count > 0),
        ];
    }

    /// <summary>
    /// When <paramref name="search"/> lists nothing, the one word nearest to the first word typed that lists nothing alone,
    /// by the letters to change, among the names, types and synonyms in the language of <paramref name="texts"/> that list
    /// something, as long as it changes at most a third of the letters typed; else null.
    /// </summary>
    public static string? Suggest(IReadOnlyList<CatalogDevice> devices, string search, Texts? texts = null)
    {
        var language = texts ?? Texts.English;
        if (WordsOf(search).FirstOrDefault(word => Filter(devices, word, [], language).Count == 0) is not { } typed)
        {
            return null;
        }

        var vocabulary = devices
            .SelectMany(device => device.Sensors.SelectMany(row =>
                new[] { row.Sensor.Name, row.Name.Shown, device.Name, language.Of(row.Reading.Kind) }.Concat(SynonymsOf(device, row, language))))
            .SelectMany(text => text.Split(NotLetters, StringSplitOptions.RemoveEmptyEntries))
            .Where(word => word.Length > 1)
            .Select(word => word.ToLower(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal);
        var lower = typed.ToLower(CultureInfo.InvariantCulture);
        return vocabulary
            .Where(word => Filter(devices, word, [], language).Count > 0)
            .Select(word => (Word: word, Distance: Distance(lower, word)))
            .Where(near => near.Distance <= Math.Max(1, lower.Length / 3))
            .OrderBy(near => near.Distance)
            .ThenBy(near => near.Word, StringComparer.Ordinal)
            .Select(near => near.Word)
            .FirstOrDefault();
    }

    private static string[] WordsOf(string search) => search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // A word matches where a word of the text starts, so "ip" finds "Local IP" and not "Chipset"; a probable name's note
    // counts, so "likely" finds every probable name as its row reads.
    private static bool Matches(CatalogDevice device, CatalogSensor row, string word, Texts texts) =>
        new[]
        {
            row.Sensor.Name,
            row.Name.Shown,
            device.Name,
            row.Sensor.Type,
            texts.Of(row.Reading.Kind),
            SensorNames.NoteOf(row.Name, texts, CultureInfo.InvariantCulture) ?? "",
        }
            .Any(text => StartsAWord(text, word))
        || SynonymsOf(device, row, texts)
            .Concat(SynonymsOf(device, row, Texts.English))
            .Any(synonym => synonym.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    // A word starts at the text's start, after a sign or a space, or as digits after letters, as "5060" in "RTX5060".
    private static bool StartsAWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (at == 0 || !char.IsLetterOrDigit(text[at - 1]) || (char.IsDigit(word[0]) && !char.IsDigit(text[at - 1])))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SynonymsOf(CatalogDevice device, CatalogSensor row, Texts texts) =>
        new[] { Enum.Parse<Synonyms>(row.Reading.Kind.ToString()) }
            .Concat(device.Kind is { } kind ? [Enum.Parse<Synonyms>(kind.ToString())] : [])
            .SelectMany(synonyms => texts.Of(synonyms).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // The letters to add, drop or change to turn one word into the other.
    private static int Distance(string from, string to)
    {
        var previous = Enumerable.Range(0, to.Length + 1).ToArray();
        for (var row = 1; row <= from.Length; row++)
        {
            var current = new int[to.Length + 1];
            current[0] = row;
            for (var column = 1; column <= to.Length; column++)
            {
                var change = from[row - 1] == to[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1], previous[column]) + 1, previous[column - 1] + change);
            }

            previous = current;
        }

        return previous[to.Length];
    }

    /// <summary>
    /// Whether a sensor shows something now: a fan turning, a throughput moving, a temperature that does not read 0 °C, or
    /// any other usable value. The window keeps the ids of those it has seen so since it opened, for <see cref="Hide"/>.
    /// </summary>
    public static bool Lively(CatalogSensor row) => row.Reading.Usable is { } value
        && (row.Reading.Kind is not (SensorKind.Fan or SensorKind.Throughput) || value > 0)
        && (row.Reading.Kind != SensorKind.Temperature || value >= 0.5);

    /// <summary>
    /// The devices without the rows nobody would add, and how many those are: a sensor that reads nothing; a fan stopped or
    /// a temperature at 0 °C that is not lively now nor among <paramref name="seen"/>, the sensors seen lively since the
    /// window opened, as a memory module's limits; a network adapter none of whose speeds has moved, as Windows' virtual
    /// ones; a GPU's D3D engine loads, which Windows' counters repeat beside the GPU's own; and a CPU's effective clocks,
    /// the time its cores ran rather than their speed, and its bus speed, which never moves. A CPU sensor that needs the
    /// driver stays, since its row says how to fix it. A device left with no row is left out.
    /// </summary>
    public static (IReadOnlyList<CatalogDevice> Shown, int Hidden) Hide(IReadOnlyList<CatalogDevice> devices, IReadOnlySet<string> seen)
    {
        var shown = devices
            .Select(device => device with { Sensors = Idle(device, seen) ? [] : [.. device.Sensors.Where(row => !Hidden(row, seen))] })
            .Where(device => device.Sensors.Count > 0)
            .ToList();
        return (shown, devices.Sum(device => device.Sensors.Count) - shown.Sum(device => device.Sensors.Count));
    }

    private static bool Idle(CatalogDevice device, IReadOnlySet<string> seen) =>
        device.Kind == DeviceKind.Network && !device.Sensors.Any(row => row.Reading.Kind == SensorKind.Throughput && Seen(row, seen));

    private static bool Seen(CatalogSensor row, IReadOnlySet<string> seen) => Lively(row) || seen.Contains(row.Id);

    private static bool Hidden(CatalogSensor row, IReadOnlySet<string> seen) =>
        Useless(row) || ((row.Why is null or NoValue.Stopped) && (row.Reading.Kind is SensorKind.Fan or SensorKind.Temperature) && !Seen(row, seen));

    // What nobody would pick whatever it read since the window opened.
    private static bool Useless(CatalogSensor row) =>
        row.Why == NoValue.ReadsNothing
        || (SensorMap.IsGpu(row.Sensor) && row.Reading.Kind == SensorKind.Load && row.Sensor.Name.StartsWith(D3dLayer, StringComparison.Ordinal))
        || (row.Reading.Kind == SensorKind.Clock
            && row.Sensor.HardwareType == "Cpu"
            && (row.Sensor.Name.Contains(Effective, StringComparison.Ordinal) || row.Sensor.Name == BusSpeed));

    /// <summary>
    /// The ids the Sensor dropdown does not offer: the rows the catalog hides whatever was seen, as a sensor that reads
    /// nothing, a GPU's D3D engine loads or a CPU's effective clocks, and a second sensor of a device with the type and the
    /// name of one before it, which nobody could tell apart. A stopped fan stays, since a CPU fan may rest at idle.
    /// </summary>
    public static IReadOnlyList<string> Unoffered(IReadOnlyList<CatalogDevice> devices) =>
    [
        .. devices.SelectMany(device => device.Sensors
            .Where((row, at) => Useless(row)
                || device.Sensors.Take(at).Any(before => before.Reading.Kind == row.Reading.Kind && before.Name.Shown == row.Name.Shown))
            .Select(row => row.Id)),
    ];

    // A type thermi does not show has no unit to show it in, so it has no row; nor has a constant.
    private static CatalogSensor? Row(string id, RawSensor sensor, SensorName name) =>
        SensorMap.ReadingOf(sensor) is { } reading && !Constant(sensor) ? new CatalogSensor(id, sensor, reading, Why(sensor, reading), name) : null;

    /// <summary>
    /// Whether a sensor only reports a fixed fact, which reads as a temperature, a load or a size but never moves: a drive's
    /// limits and wear counters, a memory module's thermal limits, resolution and size. It is no sensor to show or pick.
    /// </summary>
    public static bool Constant(RawSensor sensor) =>
        (sensor.HardwareType == Storage && (sensor.Type == Level || Array.IndexOf(DriveLimits, sensor.Name) >= 0))
        || (sensor.Id.Contains(MemoryModule, StringComparison.Ordinal)
            && Array.Exists(ModuleConstants, word => sensor.Name.Contains(word, StringComparison.Ordinal)));

    private static NoValue? Why(RawSensor sensor, Reading reading) =>
        SensorMap.ZeroIsMissing(sensor) ? NoValue.DriverMayBeMissing
        : reading.Usable is null ? NoValue.ReadsNothing
        : reading is { Kind: SensorKind.Fan, Usable: 0 } ? NoValue.Stopped
        : null;
}
