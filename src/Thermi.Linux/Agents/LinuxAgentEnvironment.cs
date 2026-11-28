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

    private readonly UserUnit _unit = new(time);
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

    // The running overlay says whether its surfaces show. With none, one that ran here and found none, as on Weston, or
    // found no gtk4-layer-shell to show them through, says why; GNOME shows it only through thermi's extension, on; whether
    // another desktop can, through layer-shell or X11, only the overlay's own surfaces find, so it says it can.
    public NoOverlayCause? OverlayCannotShow() => State() is { } state
        ? state.CanShow ? null : NotShown()
        : FoundSurfaces.Read() switch
        {
            SurfaceKind.NoLayerShell => NoOverlayCause.LayerShellMissing,
            SurfaceKind.None or SurfaceKind.NoDisplay => NoOverlayCause.Desktop,
            SurfaceKind.Shell or SurfaceKind.Layer or SurfaceKind.X11 or null => DesktopSession.IsGnome() && !ExtensionOn() ? NotShown() : null,
            _ => null,
        };

    public AboutFacts About() => LinuxAbout.Read();

    // An extension a package put in place after the person signed in is one the running Shell knows nothing of: it reads
    // it at the next sign-in, which alone shows the overlays.
    private static NoOverlayCause NotShown() =>
        ExtensionInfo() is { } info && info.NChildren() == 0 && ShellLink.OnDisk() ? NoOverlayCause.SignInAgain : NoOverlayCause.Desktop;

    private static bool ExtensionOn() => ExtensionInfo() is { } info && SessionBus.Lookup(info, "state")?.GetDouble() is { } shown && (int)shown == Active;

    // What the Shell knows of thermi's extension, empty for one it never read; null where no Shell answers.
    private static GLib.Variant? ExtensionInfo() =>
        SessionBus.Call("org.gnome.Shell", "/org/gnome/Shell", "org.gnome.Shell.Extensions", "GetExtensionInfo", Extension)?.GetChildValue(0);

    private OverlayState? State()
    {
        if (_asked is not { } asked || time.GetElapsedTime(asked.At) > Asked)
        {
            _asked = asked = (time.GetTimestamp(), OverlayState.Ask());
        }

        return asked.State;
    }
}
