using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Linux.Gnome;

/// <summary>
/// The Shell's monitors as Core's <see cref="Screen"/>s. thermi's pixels are the stage's logical ones times the largest
/// monitor scale, the pixels of the Shell's own screenshot, so a place saved in the settings means the same pixel on a
/// monitor at 150 % as Windows' does; the top bar is the taskbar at its monitor's top. Each monitor is named by its maker,
/// model and serial, as Mutter's display config tells them, so a snap follows its monitor when the monitors swap places.
/// </summary>
internal sealed class ShellScreens(IReadOnlyList<ShellMonitor> monitors, IReadOnlyDictionary<(int X, int Y), string> names)
{
    /// <summary>thermi's pixels to a logical pixel of the stage.</summary>
    public double Scale { get; } = monitors.Count == 0 ? 1 : monitors.Max(monitor => monitor.Scale);

    public IReadOnlyList<ShellMonitor> Monitors => monitors;

    public IReadOnlyList<Screen> Screens => [.. monitors.Select(ScreenOf)];

    /// <summary>The monitors' names from Mutter's display config, by each logical monitor's place on the stage; none where it does not answer.</summary>
    public static IReadOnlyDictionary<(int X, int Y), string> Names(Gio.DBusConnection bus)
    {
        try
        {
            var state = bus.CallSync(
                "org.gnome.Mutter.DisplayConfig", "/org/gnome/Mutter/DisplayConfig", "org.gnome.Mutter.DisplayConfig", "GetCurrentState",
                null, null, Gio.DBusCallFlags.None, 5000, null);
            var logical = state.GetChildValue(2);
            var found = Enumerable.Range(0, (int)logical.NChildren()).Select(at => logical.GetChildValue((nuint)at)).Select(each =>
            {
                var first = each.GetChildValue(5).GetChildValue(0);
                string Field(nuint at)
                {
                    return first.GetChildValue(at).GetString(out _);
                }

                var (connector, vendor, product, serial) = (Field(0), Field(1), Field(2), Field(3));
                var id = serial.Length > 0 ? $"{vendor}-{product}-{serial}" : $"{vendor}-{product}@{connector}";
                return (At: (each.GetChildValue(0).GetInt32(), each.GetChildValue(1).GetInt32()), Id: id, Connector: connector);
            }).ToList();
            var unique = MonitorIds.Unique([.. found.Select(each => (each.Id, each.Connector))]);
            return found.Select((each, at) => (each.At, Id: unique[at])).ToDictionary(each => each.At, each => each.Id);
        }
        catch (GLib.GException)
        {
            return new Dictionary<(int X, int Y), string>();
        }
    }

    /// <summary>A point of the stage in thermi's pixels.</summary>
    public Position Thermi(double x, double y) => new((int)Math.Round(x * Scale), (int)Math.Round(y * Scale));

    /// <summary>A point in thermi's pixels on the stage.</summary>
    public (double X, double Y) Stage(Position at) => (at.X / Scale, at.Y / Scale);

    /// <summary>Whether the monitor of <paramref name="screen"/> shows a full-screen window.</summary>
    public bool FullScreen(Screen? screen) => monitors.Where((_, at) => Screens[at] == screen).Any(monitor => monitor.FullScreen);

    private Screen ScreenOf(ShellMonitor monitor, int index)
    {
        int At(double logical)
        {
            return (int)Math.Round(logical * Scale);
        }

        var bounds = new PixelRect(At(monitor.X), At(monitor.Y), At(monitor.X + monitor.Width), At(monitor.Y + monitor.Height));
        var bar = monitor.Bar > 0 ? bounds with { Bottom = At(monitor.Y + monitor.Bar) } : (PixelRect?)null;
        PixelRect? On((double Left, double Right) box)
        {
            return bar is { } top && box.Right > box.Left ? top with { Left = At(box.Left), Right = At(box.Right) } : null;
        }

        var id = monitor.Id
            ?? (names.TryGetValue(((int)Math.Round(monitor.X), (int)Math.Round(monitor.Y)), out var named) ? named : $"monitor-{index}");
        // The system menu is where the top bar's notification area is, beside which an overlay sits by default.
        return new Screen(id, monitor.Primary, bounds, bar is { } top ? bounds with { Top = top.Bottom } : bounds, monitor.Scale)
        {
            Taskbar = bar,
            Tray = On(monitor.End),
            Centre = On(monitor.Centre),
            End = On(monitor.End),
        };
    }
}
