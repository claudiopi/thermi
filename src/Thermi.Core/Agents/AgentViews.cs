using System.Text.Json.Nodes;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Agents;

/// <summary>A sensor of the catalog: its id, the name the window shows, its type, and its value now or why it has none.</summary>
public sealed record SensorAnswer(string Id, string Name, SensorKind Type, string Unit)
{
    /// <summary>The library's own name, when the window shows another.</summary>
    public string? Library { get; init; }

    public double? Value { get; init; }

    public string? Text { get; init; }

    public NoValue? Why { get; init; }
}

public sealed record DeviceAnswer(string Id, string Name, DeviceKind? Kind, IReadOnlyList<SensorAnswer> Sensors);

public sealed record SensorsAnswer(IReadOnlyList<DeviceAnswer> Devices)
{
    public int Schema { get; init; } = AgentSchema.Current;
}

/// <summary>The settings as thermi reads them, in the file's own format, or why the file cannot be used.</summary>
public sealed record ConfigAnswer(JsonNode Settings, ConfigProblemKind? Problem)
{
    public int Schema { get; init; } = AgentSchema.Current;

    /// <summary>Where in the file it is broken, and why, in English.</summary>
    public string? Detail { get; init; }
}

/// <summary>An edit made, or refused with its refusal's key, as "Refusal.NotFound", and the refusal in English.</summary>
public sealed record EditAnswer(bool Done)
{
    public int Schema { get; init; } = AgentSchema.Current;

    public string? Refusal { get; init; }

    public string? Message { get; init; }

    /// <summary>What an undo or a redo took back or made again, in English, or null when there was nothing to.</summary>
    public string? Step { get; init; }
}

public sealed record MonitorsAnswer(IReadOnlyList<MonitorView> Monitors)
{
    public int Schema { get; init; } = AgentSchema.Current;

    /// <summary>Why there are no monitors, where only the overlay measures them, as on Linux; null where they were read.</summary>
    public ProblemAnswer? Problem { get; init; }
}

/// <summary>The saved looks by name, or one look's file as text.</summary>
public sealed record LooksAnswer(IReadOnlyList<string> Looks)
{
    public int Schema { get; init; } = AgentSchema.Current;

    public string? File { get; init; }
}

/// <summary>The fan the finder found, or why it found none, and whether it became the CPU fan.</summary>
public sealed record FinderAnswer(FanFinderOutcome? Outcome, bool Used)
{
    public int Schema { get; init; } = AgentSchema.Current;

    public string? Fan { get; init; }

    public string? Name { get; init; }

    /// <summary>Why the finder could not start, as with no fan to watch.</summary>
    public FanFinderRefusal? CannotStart { get; init; }

    /// <summary>Why the fan found did not become the CPU fan, as an edit's refusal key, as with no CPU fan item to take it.</summary>
    public string? Refusal { get; init; }

    /// <summary>Seconds until the finder runs again, when it ran too lately to load the CPU once more.</summary>
    public int? RunsAgainIn { get; init; }
}

/// <summary>Whether the overlay starts at sign-in, and whether there is an overlay to start.</summary>
public sealed record StartAnswer(bool On, bool Overlay)
{
    public int Schema { get; init; } = AgentSchema.Current;
}

public static class AgentViews
{
    /// <summary>
    /// The catalog, with the user's names, filtered as the window's search finds and by type when either is given; an
    /// address, which says where the PC is, is listed with its text only when its type is asked for.
    /// </summary>
    public static SensorsAnswer Sensors(Snapshot snapshot, ThermiConfig config, string? search, IReadOnlyCollection<SensorKind> kinds)
    {
        var devices = SensorCatalog.Build(snapshot, config.SensorNames, Texts.English);
        var found = string.IsNullOrWhiteSpace(search) && kinds.Count == 0 ? devices : SensorCatalog.Filter(devices, search ?? "", kinds, Texts.English);
        var addresses = kinds.Contains(SensorKind.Address);
        return new SensorsAnswer(
            [.. found.Select(device => new DeviceAnswer(device.Id, device.Name, device.Kind, [.. device.Sensors.Select(row => Of(row, addresses))]))]);
    }

    public static EditAnswer Edited(EditResult result) => result.RefusalKey is { } key
        ? new EditAnswer(Done: false) { Refusal = key, Message = Texts.English.All[key] }
        : new EditAnswer(Done: true);

    private static SensorAnswer Of(CatalogSensor row, bool addresses) => new(row.Id, row.Name.Shown, row.Reading.Kind, AgentReads.UnitOf(row.Reading.Kind))
    {
        Library = row.Name.Shown == row.Sensor.Name ? null : row.Sensor.Name,
        Value = row.Reading.Kind == SensorKind.Address ? null : row.Reading.Usable,
        Text = addresses && row.Reading.Kind == SensorKind.Address && row.Reading.Usable is { } address ? SystemSensors.AddressText(address) : null,
        Why = row.Why,
    };
}
