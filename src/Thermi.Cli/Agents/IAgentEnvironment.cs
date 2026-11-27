using Microsoft.Extensions.Logging;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Cli.Agents;

/// <summary>
/// What an agent's command reaches outside thermi's own logic, which each OS's host answers and tests replace: the
/// settings file, the sensors, read with a new connection for each read, whether the service is installed or this is a
/// per-user install, whether an overlay runs, whether this install is the sensors alone, the start at sign-in, the
/// monitors, the keys AltGr types with, the fan finder's load and the file that says when it last ran, and the painter.
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

    /// <summary>
    /// Overlay <paramref name="overlay"/> of <paramref name="settings"/>, counted from 0, painted as it draws
    /// <paramref name="snapshot"/>, at its size times <paramref name="scale"/>, with the OS's own painter.
    /// </summary>
    OverlayPng Paint(ThermiConfig settings, int overlay, Snapshot snapshot, float scale);
}

/// <summary>A picture as a PNG file holds it, <see cref="Width"/> by <see cref="Height"/> pixels.</summary>
internal sealed record OverlayPng(byte[] Png, int Width, int Height);
