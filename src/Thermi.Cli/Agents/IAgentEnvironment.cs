using Microsoft.Extensions.Logging;
using Thermi.Core.Agents;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;

namespace Thermi.Cli.Agents;

/// <summary>
/// What an agent's command reaches outside thermi's own logic, which each OS's host answers and tests replace: the
/// settings file, the sensors, read with a new connection for each read, whether the service is installed or this is a
/// per-user install, whether an overlay runs, whether this install is the sensors alone, the start at sign-in, the
/// monitors, the keys AltGr types with, and the fan finder's load and the file that says when it last ran.
/// </summary>
internal interface IAgentEnvironment
{
    string SettingsPath { get; }

    ILogger Log { get; }

    IStartAtSignIn Startup { get; }

    string FinderRan { get; }

    ISensorFeed Sensors();

    bool Installed();

    /// <summary>Whether this is a per-user install with no service, whose own host the sensors are read from.</summary>
    bool PerUser();

    /// <summary>Whether an overlay runs in this session.</summary>
    bool Overlay();

    bool SensorsOnly();

    /// <summary>Whether this process runs with raised rights, as from an elevated terminal, which only reads.</summary>
    bool Raised();

    /// <summary>Whether this process runs the installed version, so a newer settings file is a rollback to it.</summary>
    bool RunsInstalled();

    IReadOnlyList<Screen> Screens();

    IReadOnlySet<int> AltGrKeys();

    /// <summary>Loads every core until <paramref name="stop"/>, in a process of its own, for the fan finder.</summary>
    void Load(CancellationToken stop);

    /// <summary>Whether a file failed to open because another process holds it, as each OS says so in its own error.</summary>
    bool HeldElsewhere(IOException error);

    /// <summary>The OS this runs on, which says whether the frames of the game in front are read and which fixes a person reads.</summary>
    AgentOs Os => AgentOs.Windows;

    /// <summary>Whether this desktop can show an overlay at all, which every Windows desktop can.</summary>
    bool OverlayCanShow() => true;
}
