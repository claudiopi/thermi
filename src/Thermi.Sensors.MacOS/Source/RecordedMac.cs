using System.Globalization;
using System.Text.Json.Nodes;

namespace Thermi.Sensors.MacOS.Source;

/// <summary>
/// A recording <c>thermi-sensors --raw</c> made: a folder for each second, from 0, holding what each source answered then
/// (<see cref="MacJson"/>). It replays its seconds in turn, and from the first again after the last, so a test's service
/// serves as long as it is asked. A key, a class or a file that is gone reads as the Mac reads what it lacks: missing.
/// </summary>
internal sealed class RecordedMac : IMac
{
    private readonly int _seconds;
    private int _second = -1;
    private JsonObject _smc = [];
    private JsonObject _registry = [];
    private SystemAnswers _system = new();

    public RecordedMac(string recording)
    {
        Recording = recording;
        _seconds = Directory.EnumerateDirectories(recording)
            .Count(folder => int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out _));
        if (_seconds == 0)
        {
            throw new InvalidDataException($"{recording} holds no recorded second: record one with thermi-sensors --raw.");
        }
    }

    private string Recording { get; }

    private string Second => Path.Combine(Recording, _second.ToString(CultureInfo.InvariantCulture));

    public IReadOnlyList<string> SmcKeys() => [.. _smc.Select(pair => pair.Key)];

    public double? Smc(string key) => MacJson.Number(_smc[key]);

    public IReadOnlyList<HidReading> Hid() => MacJson.ReadHid(Text(MacJson.HidFile));

    public IReadOnlyList<ReportChannel> Report() => MacJson.ReadReport(Text(MacJson.ReportFile));

    public IReadOnlyList<JsonObject> Registry(string className, IReadOnlyList<string> keys) =>
        _registry[className] is JsonArray entries
            ? [.. entries.OfType<JsonObject>().Select(entry => Picked(entry, keys)).OfType<JsonObject>()]
            : [];

    public string? Sysctl(string name) =>
        _system.Sysctl.GetValueOrDefault(name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public long? SysctlNumber(string name) =>
        _system.Sysctl.GetValueOrDefault(name) is JsonValue value && value.TryGetValue<long>(out var number) ? number : null;

    public IReadOnlyList<long[]>? CpuTicks() => _system.CpuTicks;

    public MemoryPages? Memory() => _system.Memory;

    public IReadOnlyList<Adapter> Adapters() => MacJson.ReadAdapters(Text(MacJson.NetworkFile));

    public Moment? Now() => _system.Moment;

    public void Next()
    {
        _second = (_second + 1) % _seconds;
        _smc = MacJson.ReadObject(Text(MacJson.SmcFile));
        _registry = MacJson.ReadObject(Text(MacJson.RegistryFile));
        _system = MacJson.ReadSystem(Text(MacJson.SystemFile));
    }

    // An entry answers only the keys asked, as IOKit does; one that holds none of them is no service of what was asked.
    private static JsonObject? Picked(JsonObject entry, IReadOnlyList<string> keys)
    {
        var picked = new JsonObject();
        foreach (var key in keys.Where(entry.ContainsKey))
        {
            picked[key] = entry[key]?.DeepClone();
        }

        if (picked.Count == 0)
        {
            return null;
        }

        picked[MacJson.EntryName] = entry[MacJson.EntryName]?.DeepClone();
        return picked;
    }

    private string? Text(string file)
    {
        try
        {
            return File.ReadAllText(Path.Combine(Second, file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
