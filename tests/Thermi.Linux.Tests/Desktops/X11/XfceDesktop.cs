using System.Globalization;

namespace Thermi.Linux.Tests.Desktops.X11;

/// <summary>
/// An X11 desktop as Xfce's: Xorg on its dummy driver, Xfce's window manager, and its panel along an edge of the primary
/// monitor, which takes its room off the work area.
/// </summary>
internal sealed class XfceDesktop
{
    /// <summary>The room the panel keeps off the work area, in the X server's pixels: a pixel past its size, as its struts say.</summary>
    public const int PanelRoom = PanelSize + 1;

    private const int PanelSize = 40;

    private readonly OwnSession _session;
    private SessionProcess? _panel;

    private XfceDesktop(OwnSession session)
    {
        _session = session;
    }

    /// <summary>The desktop, once the panel's room is off the work area, as a test may measure it.</summary>
    public static async Task<XfceDesktop> StartAsync(OwnSession session, IReadOnlyList<TestMonitor> monitors, PanelEdge edge, CancellationToken cancel)
    {
        var channels = Directory.CreateDirectory(Path.Combine(session.Variables["XDG_CONFIG_HOME"], "xfce4", "xfconf", "xfce-perchannel-xml"));
        await File.WriteAllTextAsync(Path.Combine(channels.FullName, "xfce4-panel.xml"), Panel(edge), cancel).ConfigureAwait(false);
        // Xfce's programs keep their settings in xfconfd, which the session's bus starts when they ask.
        session.Activates("org.xfce.Xfconf");
        using var saved = Saved(channels.FullName);
        await XServers.StartXorgAsync(session, monitors, cancel).ConfigureAwait(false);
        await session.StartAsync("xfwm4", [], cancel: cancel).ConfigureAwait(false);
        await saved.SavedAsync(cancel).ConfigureAwait(false);
        var desktop = new XfceDesktop(session);
        await desktop.PanelAsync(() => Task.CompletedTask, monitors, bare: true, cancel).ConfigureAwait(false);
        return desktop;
    }

    /// <summary>
    /// Swaps the monitors, and starts the panel again on the primary one: the panel follows it as it moves, but its room
    /// leaves the work area for a while that nothing tells the end of, while a panel that starts says it through the work area.
    /// </summary>
    public Task SwapAsync(IReadOnlyList<TestMonitor> monitors, CancellationToken cancel) =>
        PanelAsync(
            async () =>
            {
                _panel!.Kill();
                await _panel.WaitForExitAsync(cancel).ConfigureAwait(false);
                await XServers.SetOutputsAsync(_session, monitors, swapped: true, cancel).ConfigureAwait(false);
            },
            monitors,
            bare: false,
            cancel);

    // Xfce's panel as a person's first sign-in leaves it, so it asks nothing: one panel the whole length of an edge of the
    // primary monitor, with a separator alone, as a window's button would change under an overlay as the window in front
    // changes, and no tray of its own, which would take the tests' StatusNotifierWatcher's name. Its position is a corner
    // or an edge it snaps to, as xfce4-panel numbers them: 6 the top-left corner, 8 the bottom-left, 5 the left edge; its
    // mode 0 lies along the top or the bottom, 1 along a side.
    private static string Panel(PanelEdge edge)
    {
        var (position, mode) = edge switch
        {
            PanelEdge.Top => (6, 0),
            PanelEdge.Bottom => (8, 0),
            PanelEdge.Left => (5, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, "Unknown edge."),
        };
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <channel name="xfce4-panel" version="1.0">
              <property name="configver" type="int" value="2"/>
              <property name="panels" type="array">
                <value type="int" value="1"/>
                <property name="panel-1" type="empty">
                  <property name="position" type="string" value="p={position};x=0;y=0"/>
                  <property name="mode" type="uint" value="{mode}"/>
                  <property name="length" type="uint" value="100"/>
                  <property name="position-locked" type="bool" value="true"/>
                  <property name="size" type="uint" value="{PanelSize}"/>
                  <property name="output-name" type="string" value="Primary"/>
                  <property name="plugin-ids" type="array">
                    <value type="int" value="1"/>
                  </property>
                </property>
              </property>
              <property name="plugins" type="empty">
                <property name="plugin-1" type="string" value="separator"/>
              </property>
            </channel>
            """;
    }

    // Xfwm4 takes the panel's room off the work area after the panel is up: it is the whole screen while no panel is, then
    // the screen less the panel. xprop spies on every property of the root, as it ends at once on one not set yet, and
    // writes each change at once only with its output a line at a time.
    private async Task PanelAsync(Func<Task> before, IReadOnlyList<TestMonitor> monitors, bool bare, CancellationToken cancel)
    {
        var whole = (0, 0, monitors.Sum(monitor => monitor.Width), monitors.Max(monitor => monitor.Height));
        var spy = await _session.StartAsync("stdbuf", ["-oL", "xprop", "-root", "-spy"], cancel: cancel).ConfigureAwait(false);
        await before().ConfigureAwait(false);
        _panel = await _session.StartAsync("xfce4-panel", [], cancel: cancel).ConfigureAwait(false);
        while (await spy.Out.ReadLineAsync(cancel).ConfigureAwait(false) is { } line)
        {
            if (WorkArea(line) is { } area)
            {
                bare |= area == whole;
                if (bare && area != whole)
                {
                    break;
                }
            }
        }

        spy.Kill();
    }

    // _NET_WORKAREA(CARDINAL) = x, y, width, height, as many times as there are workspaces.
    // xfwm4 writes its defaults into its empty channel as it starts, which xfconfd saves to its file a moment later: the
    // desktop is up once it did, so no save of it lands in the session's folder as the session deletes it.
    private static ChannelSave Saved(string channels) => new(channels, "xfwm4.xml");

    private sealed class ChannelSave : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly TaskCompletionSource _saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _file;

        public ChannelSave(string folder, string name)
        {
            _file = Path.Combine(folder, name);
            _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName };
            _watcher.Renamed += (_, args) => Seen(args.Name);
            _watcher.Created += (_, args) => Seen(args.Name);
            _watcher.EnableRaisingEvents = true;

            void Seen(string? file)
            {
                if (file == name)
                {
                    _saved.TrySetResult();
                }
            }
        }

        public async Task SavedAsync(CancellationToken cancel)
        {
            if (File.Exists(_file))
            {
                return;
            }

            try
            {
                await _saved.Task.WaitAsync(Bound.Line, TimeProvider.System, cancel).ConfigureAwait(false);
            }
            catch (TimeoutException late)
            {
                throw new TimeoutException($"xfconfd saved no {_file} in {Bound.Line.TotalSeconds:0} s.", late);
            }
        }

        public void Dispose() => _watcher.Dispose();
    }
    private static (int, int, int, int)? WorkArea(string line)
    {
        if (!line.StartsWith("_NET_WORKAREA(", StringComparison.Ordinal) || line.Split('=') is not [_, var values])
        {
            return null;
        }

        var area = values.Split(',').Take(4).Select(value => int.Parse(value.Trim(), CultureInfo.InvariantCulture)).ToArray();
        return area is [var x, var y, var width, var height] ? (x, y, width, height) : null;
    }
}
