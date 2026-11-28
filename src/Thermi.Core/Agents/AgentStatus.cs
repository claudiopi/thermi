using Thermi.Core.Bridge;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Core.Agents;

/// <summary>What stops an agent's answers, each said to the agent with its cause and what a person does about it.</summary>
public enum AgentProblem
{
    ServiceMissing,

    ServiceStopped,

    /// <summary>The service runs and sent nothing for a while.</summary>
    ServiceStale,

    PipeTaken,

    /// <summary>A CPU temperature, power or clock of exactly zero.</summary>
    DriverMissing,

    SettingsBroken,

    /// <summary>A per-user install whose own host serves the sensors only while thermi runs, and none runs.</summary>
    NotRunning,

    /// <summary>An OS whose games' frames thermi does not read, as Linux: FPS and frame time read missing.</summary>
    FramesNotRead,

    /// <summary>A desktop where no overlay can show, as GNOME without thermi's extension.</summary>
    OverlayCannotShow,

    /// <summary>No overlay runs to tell the monitors, which on Linux only the overlay measures, as <c>monitors</c> says.</summary>
    OverlayNotRunning,
}

/// <summary>Why no overlay can show on a desktop, which the person fixes in different ways.</summary>
public enum NoOverlayCause
{
    /// <summary>The desktop offers nothing thermi shows an overlay on, as GNOME without thermi's extension.</summary>
    Desktop,

    /// <summary>On Linux, gtk4-layer-shell is not installed, which thermi shows its overlays through off GNOME on Wayland.</summary>
    LayerShellMissing,
}

/// <summary>Each problem's cause and fix, as an agent reads them; the contract answers them in English.</summary>
[Translated]
public enum AgentText
{
    ServiceMissingCause,
    ServiceMissingFix,
    ServiceStoppedCause,
    ServiceStoppedFix,
    ServiceStaleCause,
    ServiceStaleFix,
    PipeTakenCause,
    PipeTakenFix,
    DriverMissingCause,
    DriverMissingFix,
    SettingsBrokenCause,
    SettingsBrokenFix,
    ServiceStoppedFixSensorsOnly,
    DriverMissingFixSensorsOnly,
    SettingsBrokenFixSensorsOnly,
    NotRunningCause,
    NotRunningFix,
    FramesNotReadCause,
    FramesNotReadFix,
    OverlayCannotShowCause,
    OverlayCannotShowFix,
    ServiceStoppedFixLinux,
    ServiceStaleFixLinux,
    PipeTakenCauseLinux,
    PipeTakenFixLinux,
    DriverMissingFixLinux,
    SettingsBrokenFixLinuxSensorsOnly,
    DriverMissingCauseLinux,
    OverlayNotRunningCause,
    OverlayNotRunningFix,
    OverlayCannotShowCauseLayerShell,
    OverlayCannotShowFixLayerShell,
}

/// <summary>A problem, with why it happens and what a person does about it; nothing an agent may do itself.</summary>
public sealed record ProblemAnswer(AgentProblem Code, string Cause, string Fix);

/// <summary>Whether the service runs and sends fresh readings, an overlay runs, and what stops the answers.</summary>
public sealed record StatusAnswer(ServiceState Service, bool Overlay, bool SensorsOnly, IReadOnlyList<ProblemAnswer> Problems)
{
    public int Schema { get; init; } = AgentSchema.Current;

    /// <summary>The settings file's problem, or null when it can be used.</summary>
    public ConfigProblemKind? Settings { get; init; }

    /// <summary>thermi's version, as About says it, or null where the answer does not say it.</summary>
    public string? Version { get; init; }

    /// <summary>The update channel thermi follows, stable or beta, as About says it, or null where the answer does not say it.</summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Where updates come from, as About says it: <c>thermi</c>, its own updater, or <c>system</c>, the system's package
    /// manager; null where the answer does not say it.
    /// </summary>
    public string? Updates { get; init; }
}

/// <summary>What the service, the pipe, the readings and the settings say, as the status decides it.</summary>
public sealed record AgentFacts(ServiceState Service, bool Installed, bool Taken, Snapshot? Latest, ConfigProblem? Settings)
{
    public bool Overlay { get; init; }

    public bool SensorsOnly { get; init; }

    /// <summary>Whether this is a per-user install, which has no service: the user's own host serves while thermi runs.</summary>
    public bool PerUser { get; init; }

    public HostOs Os { get; init; }

    /// <summary>Why no overlay can show on this desktop; null where one can, as on every Windows desktop.</summary>
    public NoOverlayCause? OverlayCannotShow { get; init; }

    /// <summary>What About says of the version, the channel and where updates come from, which status says too.</summary>
    public AboutFacts About { get; init; } = AboutFacts.None;
}

/// <summary>
/// thermi's version, the update channel it follows, and where its updates come from, <c>thermi</c> or <c>system</c>; each
/// null where the host does not say it.
/// </summary>
public sealed record AboutFacts(string? Version, string? Channel, string? Updates)
{
    public static AboutFacts None { get; } = new(null, null, null);
}

public static class AgentStatus
{
    public static StatusAnswer Of(AgentFacts facts)
    {
        var problems = new List<AgentProblem>();
        problems.AddRange(facts switch
        {
            { Installed: false, PerUser: false } => [AgentProblem.ServiceMissing],
            { Taken: true } => [AgentProblem.PipeTaken],
            { Service: ServiceState.Stopped, PerUser: true } => [AgentProblem.NotRunning],
            { Service: ServiceState.Stopped } => [AgentProblem.ServiceStopped],
            { Service: ServiceState.Stale } => [AgentProblem.ServiceStale],
            _ => [],
        });
        // On Linux a CPU whose module is not loaded has no temperature at all, where Windows' reads zero without its driver.
        if (facts.Latest is { } latest && (SensorMap.DriverMayBeMissing(latest) || (facts.Os == HostOs.Linux && !SensorMap.HasCpuTemperature(latest))))
        {
            problems.Add(AgentProblem.DriverMissing);
        }

        if (facts.Settings is { Kind: not ConfigProblemKind.Missing })
        {
            problems.Add(AgentProblem.SettingsBroken);
        }

        if (facts.Os == HostOs.Linux)
        {
            problems.Add(AgentProblem.FramesNotRead);
        }

        if (facts.OverlayCannotShow is not null)
        {
            problems.Add(AgentProblem.OverlayCannotShow);
        }

        var said = problems.Select(problem => Said(problem, facts.SensorsOnly, facts.Os, facts.OverlayCannotShow));
        return new StatusAnswer(facts.Service, facts.Overlay, facts.SensorsOnly, [.. said])
        {
            Settings = facts.Settings is { Kind: not ConfigProblemKind.Missing } problem ? problem.Kind : null,
            Version = facts.About.Version,
            Channel = facts.About.Channel,
            Updates = facts.About.Updates,
        };
    }

    /// <summary>The problem that keeps the service's readings from a read, or null when they are fresh.</summary>
    public static ProblemAnswer? Unread(AgentFacts facts) =>
        Of(facts).Problems.FirstOrDefault(problem => problem.Code is AgentProblem.ServiceMissing or AgentProblem.PipeTaken or AgentProblem.NotRunning
            or AgentProblem.ServiceStopped or AgentProblem.ServiceStale);

    /// <summary>
    /// A problem's cause and fix; with the sensors alone installed, a fix sends the person to no menu or window, and on
    /// Linux to systemd and the package rather than to Windows; an overlay that cannot show, to what <paramref name="noOverlay"/> lacks.
    /// </summary>
    public static ProblemAnswer Said(AgentProblem problem, bool sensorsOnly = false, HostOs os = HostOs.Windows, NoOverlayCause? noOverlay = null)
    {
        var (cause, fix) = (os == HostOs.Linux ? OnLinux(problem, sensorsOnly, noOverlay) : null) ?? problem switch
        {
            AgentProblem.ServiceMissing => (AgentText.ServiceMissingCause, AgentText.ServiceMissingFix),
            AgentProblem.ServiceStopped when sensorsOnly => (AgentText.ServiceStoppedCause, AgentText.ServiceStoppedFixSensorsOnly),
            AgentProblem.DriverMissing when sensorsOnly => (AgentText.DriverMissingCause, AgentText.DriverMissingFixSensorsOnly),
            AgentProblem.SettingsBroken when sensorsOnly => (AgentText.SettingsBrokenCause, AgentText.SettingsBrokenFixSensorsOnly),
            AgentProblem.ServiceStopped => (AgentText.ServiceStoppedCause, AgentText.ServiceStoppedFix),
            AgentProblem.ServiceStale => (AgentText.ServiceStaleCause, AgentText.ServiceStaleFix),
            AgentProblem.PipeTaken => (AgentText.PipeTakenCause, AgentText.PipeTakenFix),
            AgentProblem.DriverMissing => (AgentText.DriverMissingCause, AgentText.DriverMissingFix),
            AgentProblem.SettingsBroken => (AgentText.SettingsBrokenCause, AgentText.SettingsBrokenFix),
            AgentProblem.NotRunning => (AgentText.NotRunningCause, AgentText.NotRunningFix),
            AgentProblem.FramesNotRead => (AgentText.FramesNotReadCause, AgentText.FramesNotReadFix),
            AgentProblem.OverlayCannotShow => (AgentText.OverlayCannotShowCause, AgentText.OverlayCannotShowFix),
            AgentProblem.OverlayNotRunning => (AgentText.OverlayNotRunningCause, AgentText.OverlayNotRunningFix),
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown problem."),
        };
        return new ProblemAnswer(problem, Texts.English.Of(cause), Texts.English.Of(fix));
    }

    private static (AgentText Cause, AgentText Fix)? OnLinux(AgentProblem problem, bool sensorsOnly, NoOverlayCause? noOverlay) => problem switch
    {
        AgentProblem.OverlayCannotShow when noOverlay == NoOverlayCause.LayerShellMissing =>
            (AgentText.OverlayCannotShowCauseLayerShell, AgentText.OverlayCannotShowFixLayerShell),
        AgentProblem.ServiceStopped => (AgentText.ServiceStoppedCause, AgentText.ServiceStoppedFixLinux),
        AgentProblem.ServiceStale => (AgentText.ServiceStaleCause, AgentText.ServiceStaleFixLinux),
        AgentProblem.PipeTaken => (AgentText.PipeTakenCauseLinux, AgentText.PipeTakenFixLinux),
        AgentProblem.DriverMissing => (AgentText.DriverMissingCauseLinux, AgentText.DriverMissingFixLinux),
        AgentProblem.SettingsBroken when sensorsOnly => (AgentText.SettingsBrokenCause, AgentText.SettingsBrokenFixLinuxSensorsOnly),
        // Said as on Windows: they name nothing of Windows', or never happen on Linux, as a per-user install's.
        AgentProblem.ServiceMissing or AgentProblem.SettingsBroken or AgentProblem.NotRunning or AgentProblem.FramesNotRead
            or AgentProblem.OverlayCannotShow or AgentProblem.OverlayNotRunning => null,
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown problem."),
    };
}
