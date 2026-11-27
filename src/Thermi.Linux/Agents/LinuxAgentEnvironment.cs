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
using Thermi.Linux.LayerShell;
using Thermi.Linux.Overlay;
using Thermi.Linux.Sensors;
using Thermi.Linux.X11;

namespace Thermi.Linux.Agents;

/// <summary>
/// What an agent's command reaches on Linux: the sensors from <c>thermi-sensors</c>' socket, the service installed when
/// systemd has its socket unit, an overlay running when it owns its name on the session bus, the start at sign-in as its
/// user unit, GNOME's monitors, the load of a <c>thermi cpu-load</c> at the scheduler's idle priority, and the kernel's
/// answer for a file another process holds. Linux reads no game's frames.
/// </summary>
internal sealed class LinuxAgentEnvironment(ILogger log, TimeProvider time) : IAgentEnvironment
{
    /// <summary>The name the overlay owns on the session bus while it runs, its application id.</summary>
    public const string BusName = "io.github.claudiopi.thermi";

    /// <summary>The GNOME Shell extension that shows the overlay on GNOME.</summary>
    public const string Extension = "thermi@claudiopi.github.io";

    // Where the packages install thermi; a build run from anywhere else is no installed one.
    private const string InstallFolder = "/usr/lib/thermi/";

    // flock's EWOULDBLOCK, which .NET gives as the error's code when another process holds the file.
    private const int WouldBlock = 11;

    // GNOME Shell's state of an extension that is on and running.
    private const int Active = 1;

    private readonly UserUnit _unit = UserUnit.Of();

    public string SettingsPath => ConfigStore.DefaultPath;

    public ILogger Log => log;

    public IStartAtSignIn Startup => _unit;

    public string FinderRan => Path.Combine(UserFolders.Local, "thermi", "fan-finder-ran.txt");

    public ISensorFeed Sensors() => new SocketFeed(time, log);

    public bool Installed() => SocketFeed.Installed();

    public bool PerUser() => false;

    public bool Overlay() => SessionBus.HasOwner(BusName);

    /// <summary>The sensors' package alone has no overlay's unit for the session to start.</summary>
    public bool SensorsOnly() => _unit.File is null;

    // Root writes into the user's folders would leave files the user cannot change, so as root it only reads.
    public bool Raised() => SocketFeed.OwnUserId() == 0;

    public bool RunsInstalled() => AppContext.BaseDirectory.StartsWith(InstallFolder, StringComparison.Ordinal);

    // GNOME's monitors, as Mutter lays them out; another desktop's as the overlay's own surfaces find them, layer-shell's
    // or X11's.
    public IReadOnlyList<Screen> Screens() => DesktopSession.IsGnome() ? GnomeMonitors.Read() : SurfacesScreens();

    public IReadOnlySet<int> AltGrKeys() => new HashSet<int>();

    // chrt runs thermi-cli in its place, so ending the process it started ends the load; thermi-cli, which every package
    // installs, where thermi comes only with the overlay's.
    public void Load(CancellationToken stop) => CpuLoad.Start(
        "chrt",
        ["--idle", "0", Path.Combine(AppContext.BaseDirectory, Program.ConsoleDoor), .. CpuLoad.Arguments((int)FanFinder.MaxRun.TotalSeconds)],
        stop);

    public bool HeldElsewhere(IOException error) => error.HResult == WouldBlock;

    public AgentOs Os => AgentOs.Linux;

    // GNOME shows it only through thermi's extension, on; another desktop through layer surfaces or X11, as the overlay finds them.
    public bool OverlayCanShow() => DesktopSession.IsGnome()
        ? SessionBus.Call("org.gnome.Shell.Extensions", "/org/gnome/Shell/Extensions", "org.gnome.Shell.Extensions", "GetExtensionInfo", Extension)
            is { } info
            && SessionBus.Lookup(info.GetChildValue(0), "state")?.GetDouble() is { } state
            && (int)state == Active
        : DesktopSession.Start() != SurfaceKind.None;

    // The surfaces measure the monitors as the overlay's do, GTK's main loop run until they did.
    private static List<Screen> SurfacesScreens()
    {
        using IOverlaySurfaces? surfaces = DesktopSession.Start() switch
        {
            SurfaceKind.Layer => new LayerSurfaces(),
            SurfaceKind.X11 => new X11Surfaces(),
            SurfaceKind.Shell or SurfaceKind.None => null,
            _ => null,
        };
        if (surfaces is null || GdkMonitors.All().Count == 0)
        {
            return [];
        }

        var monitors = new List<ShellMonitor>();
        surfaces.Arrived = () => monitors = [.. surfaces.Monitors()];
        surfaces.Start();
        var context = GLib.MainContext.Default();
        while (monitors.Count == 0)
        {
            _ = context.Iteration(true);
        }

        return [.. new ShellScreens(monitors, new Dictionary<(int X, int Y), string>()).Screens];
    }

    public AboutFacts About() => LinuxAbout.Read();
}
