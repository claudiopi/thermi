using Microsoft.Extensions.Logging;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Cli.Agents;
using Thermi.Core.Agents;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Overlay;
using Thermi.Linux.Sensors;

namespace Thermi.Linux.Agents;

/// <summary>
/// What an agent's command reaches on Linux: the sensors from <c>thermi-sensors</c>' socket, the service installed when
/// systemd has its socket unit, an overlay running when it owns its name on the session bus, the start at sign-in as its
/// user unit, the monitors as the running overlay measures them, the load of a <c>thermi cpu-load</c> at the scheduler's
/// idle priority, and the kernel's answer for a file another process holds. Linux reads no game's frames. It asks only
/// through Gio, never GTK, so it answers with the sensors' package alone and over SSH, where there is no display.
/// </summary>
internal sealed class LinuxAgentEnvironment(ILogger log, TimeProvider time) : IAgentEnvironment
{
    /// <summary>The GNOME Shell extension that shows the overlay on GNOME.</summary>
    public const string Extension = "thermi@claudiopi.github.io";

    // flock's EWOULDBLOCK, which .NET gives as the error's code when another process holds the file.
    private const int WouldBlock = 11;

    // GNOME Shell's state of an extension that is on and running.
    private const int Active = 1;

    // One answer serves a command's screens, why there are none and whether the overlay can show, and the MCP server's next.
    private static readonly TimeSpan Asked = TimeSpan.FromSeconds(1);

    private readonly UserUnit _unit = new();
    private (long At, OverlayState? State)? _asked;

    public string SettingsPath => ConfigStore.DefaultPath;

    public ILogger Log => log;

    public IStartAtSignIn Startup => _unit;

    public string FinderRan => Path.Combine(UserFolders.Local, "thermi", "fan-finder-ran.txt");

    public ISensorFeed Sensors() => new SocketFeed(time, log);

    public bool Installed() => SocketFeed.Installed();

    public bool PerUser() => false;

    public bool Overlay() => SessionBus.HasOwner(ShellLink.Name);

    /// <summary>The sensors' package alone has no overlay's unit for the session to start.</summary>
    public bool SensorsOnly() => UserUnit.File is null;

    // Root writes into the user's folders would leave files the user cannot change, so as root it only reads.
    public bool Raised() => SocketFeed.OwnUserId() == 0;

    public bool RunsInstalled() => LinuxAbout.Installed;

    // The monitors as the overlay places the overlays on them, so an agent's place is the overlay's pixel on every desktop.
    public IReadOnlyList<Screen> Screens() => State()?.Screens ?? [];

    // The state the monitors were just read from: only an overlay that does not answer is asked whether it runs.
    public AgentProblem? WhyNoScreens() => State() is { } state
        ? state is { CanShow: false, Screens.Count: 0 } ? AgentProblem.OverlayCannotShow : null
        : Overlay() ? null : AgentProblem.OverlayNotRunning;

    public IReadOnlySet<int> AltGrKeys() => new HashSet<int>();

    // chrt runs thermi-cli in its place, so ending the process it started ends the load; thermi-cli, which every package
    // installs, where thermi comes only with the overlay's.
    public void Load(CancellationToken stop) => CpuLoad.Start(
        "chrt",
        ["--idle", "0", Path.Combine(AppContext.BaseDirectory, Program.ConsoleDoor), .. CpuLoad.Arguments((int)FanFinder.MaxRun.TotalSeconds)],
        stop);

    public bool HeldElsewhere(IOException error) => error.HResult == WouldBlock;

    public Core.Bridge.HostOs Os => Core.Bridge.HostOs.Linux;

    // The running overlay says whether its surfaces show. With none, one that ran here and found none, as on Weston, says
    // no; GNOME shows it only through thermi's extension, on; whether another desktop can, through layer-shell or X11, only
    // the overlay's own surfaces find, so it says yes.
    public bool OverlayCanShow() => State() is { } state
        ? state.CanShow
        : FoundSurfaces.Read() is not (SurfaceKind.None or SurfaceKind.NoDisplay)
            && (!DesktopSession.IsGnome()
                || (SessionBus.Call("org.gnome.Shell.Extensions", "/org/gnome/Shell/Extensions", "org.gnome.Shell.Extensions", "GetExtensionInfo", Extension)
                    is { } info
                    && SessionBus.Lookup(info.GetChildValue(0), "state")?.GetDouble() is { } shown
                    && (int)shown == Active));

    public AboutFacts About() => LinuxAbout.Read();

    private OverlayState? State()
    {
        if (_asked is not { } asked || time.GetElapsedTime(asked.At) > Asked)
        {
            _asked = asked = (time.GetTimestamp(), OverlayState.Ask());
        }

        return asked.State;
    }
}
