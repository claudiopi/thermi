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
        if (facts.Latest is { } latest && SensorMap.DriverMayBeMissing(latest))
        {
            problems.Add(AgentProblem.DriverMissing);
        }

        if (facts.Settings is { Kind: not ConfigProblemKind.Missing })
        {
            problems.Add(AgentProblem.SettingsBroken);
        }

        return new StatusAnswer(facts.Service, facts.Overlay, facts.SensorsOnly, [.. problems.Select(problem => Said(problem, facts.SensorsOnly))])
        {
            Settings = facts.Settings is { Kind: not ConfigProblemKind.Missing } problem ? problem.Kind : null,
        };
    }

    /// <summary>The problem that keeps the service's readings from a read, or null when they are fresh.</summary>
    public static ProblemAnswer? Unread(AgentFacts facts) =>
        Of(facts).Problems.FirstOrDefault(problem => problem.Code is not (AgentProblem.DriverMissing or AgentProblem.SettingsBroken));

    /// <summary>A problem's cause and fix; with the sensors alone installed, a fix sends the person to no menu or window.</summary>
    public static ProblemAnswer Said(AgentProblem problem, bool sensorsOnly = false)
    {
        var (cause, fix) = problem switch
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
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown problem."),
        };
        return new ProblemAnswer(problem, Texts.English.Of(cause), Texts.English.Of(fix));
    }
}
