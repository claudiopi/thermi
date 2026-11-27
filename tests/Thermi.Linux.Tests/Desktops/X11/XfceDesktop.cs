using System.Globalization;

namespace Thermi.Linux.Tests.Desktops.X11;

/// <summary>
/// An X11 desktop as Xfce's: Xorg on its dummy driver, Xfce's window manager, and its panel at the top of the primary
/// monitor, which takes its room off the work area.
/// </summary>
internal sealed class XfceDesktop
{
    // Xfce's panel as a person's first sign-in leaves it, so it asks nothing: one panel at the top of the primary monitor,
    // with a separator alone, as a window's button would change under an overlay as the window in front changes, and no
    // tray of its own, which would take the tests' StatusNotifierWatcher's name.
    private const string Panel = """
        <?xml version="1.0" encoding="UTF-8"?>
        <channel name="xfce4-panel" version="1.0">
          <property name="configver" type="int" value="2"/>
          <property name="panels" type="array">
            <value type="int" value="1"/>
            <property name="panel-1" type="empty">
              <property name="position" type="string" value="p=6;x=0;y=0"/>
              <property name="length" type="uint" value="100"/>
              <property name="position-locked" type="bool" value="true"/>
              <property name="size" type="uint" value="40"/>
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

    private readonly OwnSession _session;
    private SessionProcess? _panel;

    private XfceDesktop(OwnSession session)
    {
        _session = session;
    }

    /// <summary>The desktop, once the panel's room is off the work area, as a test may measure it.</summary>
    public static async Task<XfceDesktop> StartAsync(OwnSession session, IReadOnlyList<TestMonitor> monitors, CancellationToken cancel)
    {
        var channels = Directory.CreateDirectory(Path.Combine(session.Variables["XDG_CONFIG_HOME"], "xfce4", "xfconf", "xfce-perchannel-xml"));
        await File.WriteAllTextAsync(Path.Combine(channels.FullName, "xfce4-panel.xml"), Panel, cancel).ConfigureAwait(false);
        // Xfce's programs keep their settings in xfconfd, which the session's bus starts when they ask.
        session.Activates("org.xfce.Xfconf");
        await XServers.StartXorgAsync(session, monitors, cancel).ConfigureAwait(false);
        await session.StartAsync("xfwm4", [], cancel: cancel).ConfigureAwait(false);
        var desktop = new XfceDesktop(session);
        await desktop.PanelAsync(() => Task.CompletedTask, bare: true, cancel).ConfigureAwait(false);
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
            bare: false,
            cancel);

    // Xfwm4 takes the panel's room off the work area after the panel is up: its top is 0 while no panel is, then the
    // panel's height. xprop spies on every property of the root, as it ends at once on one not set yet, and writes each
    // change at once only with its output a line at a time.
    private async Task PanelAsync(Func<Task> before, bool bare, CancellationToken cancel)
    {
        var spy = await _session.StartAsync("stdbuf", ["-oL", "xprop", "-root", "-spy"], cancel: cancel).ConfigureAwait(false);
        await before().ConfigureAwait(false);
        _panel = await _session.StartAsync("xfce4-panel", [], cancel: cancel).ConfigureAwait(false);
        while (await spy.Out.ReadLineAsync(cancel).ConfigureAwait(false) is { } line)
        {
            if (Top(line) is { } top)
            {
                bare |= top == 0;
                if (bare && top != 0)
                {
                    break;
                }
            }
        }

        spy.Kill();
    }

    // _NET_WORKAREA(CARDINAL) = x, y, width, height, as many times as there are workspaces.
    private static int? Top(string line) =>
        line.StartsWith("_NET_WORKAREA(", StringComparison.Ordinal) && line.Split('=') is [_, var values] && values.Split(',') is [_, var top, ..]
            ? int.Parse(top.Trim(), CultureInfo.InvariantCulture)
            : null;
}
