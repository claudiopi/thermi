using System.Text.Json;
using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;

namespace Thermi.Linux.LayerShell;

/// <summary>
/// KDE Plasma's panels along the top or the bottom of a monitor, as GNOME's extension tells its top bar's boxes: the start,
/// the launcher and what sits before the task manager; the centre, the task manager, whose buttons fill it from its start;
/// and the end, the tray, the clock and what follows them. No overlay covers any of them: the task manager tells no program
/// where its buttons end, so on the default panel, where it fills what the others leave, there is no room for an overlay,
/// which shows above the panel, as a vertical one does. Plasma tells a program none of
/// it on Wayland or on its panel's surface; plasmashell's scripting does (<c>org.kde.PlasmaShell.evaluateScript</c>), each
/// widget's place on its panel, which spans its monitor where it fills it, as by default. A panel that does not fill its
/// monitor is left out, and one with no tray tells no end: an overlay there keeps Core's reserve. The tray grows as
/// programs come, thermi first, so the panels are read as the monitors change, once thermi's icons are in, and once a
/// minute.
/// </summary>
internal sealed class PlasmaPanels : IDisposable
{
    /// <summary>How often the panels are read again, as a program's tray icon comes or goes.</summary>
    public static readonly TimeSpan Again = TimeSpan.FromMinutes(1);

    // By when the tray made room for thermi's own icons, which register as thermi starts.
    private static readonly TimeSpan Settled = TimeSpan.FromSeconds(5);

    // Each panel along a monitor's top or bottom that fills it, its monitor's place, and each widget's place along it.
    private const string Script = """
        const found = [];
        for (const panel of panels()) {
          if ((panel.location !== 'top' && panel.location !== 'bottom') || panel.lengthMode !== 'fill') continue;
          const screen = screenGeometry(panel.screen);
          found.push({ location: panel.location, x: screen.x, y: screen.y, width: screen.width, height: screen.height,
            widgets: panel.widgets().map(widget => ({ type: widget.type, x: widget.geometry.x, width: widget.geometry.width })) });
        }
        print(JSON.stringify(found));
        """;

    private readonly Gio.DBusConnection _bus;
    private readonly ILogger _log;
    private readonly ITimer? _again;
    private readonly bool _runs;
    private IReadOnlyList<PlasmaPanel> _panels = [];
    private bool _unreadSaid;
    private bool _reading;
    private bool _owed;

    /// <summary>
    /// Plasma's panels, read where the session is Plasma's, as <paramref name="desktop"/> names it, and again shortly after and
    /// each <see cref="Again"/> by <paramref name="time"/>, on the main loop <paramref name="post"/> runs on.
    /// </summary>
    public PlasmaPanels(Gio.DBusConnection bus, string? desktop, TimeProvider time, Action<Action> post, ILogger log)
    {
        (_bus, _log) = (bus, log);
        _runs = (desktop ?? "").Split(':').Contains("KDE", StringComparer.OrdinalIgnoreCase);
        _again = _runs ? time.CreateTimer(_ => post(() => _ = ReadAsync()), null, Settled, Again) : null;
    }

    /// <summary>Called on the main loop each time a reading finds the panels changed.</summary>
    public Action Changed { get; set; } = () => { };

    /// <summary>The monitor with the boxes of the panels last read on it, as <see cref="Boxed(ShellMonitor, IReadOnlyList{PlasmaPanel})"/>.</summary>
    public ShellMonitor Boxed(ShellMonitor monitor) => Boxed(monitor, _panels);

    /// <summary>
    /// A monitor with the start, the centre and the end of Plasma's panel on it of <paramref name="panels"/>, as the stage
    /// has them: the panel along the edge whose room is the deeper, the top one where both keep as much, which the overlays
    /// take for the taskbar. As it was where that edge keeps no room or has no such panel.
    /// </summary>
    public static ShellMonitor Boxed(ShellMonitor monitor, IReadOnlyList<PlasmaPanel> panels)
    {
        var room = monitor.Room ?? new PanelRoom(0, monitor.Bar, 0, 0);
        var bottom = room.Bottom > room.Top;
        var place = ((int)Math.Round(monitor.X), (int)Math.Round(monitor.Y), (int)Math.Round(monitor.Width), (int)Math.Round(monitor.Height));
        return Math.Max(room.Top, room.Bottom) > 0 && panels.FirstOrDefault(panel => panel.Place == place && panel.Bottom == bottom) is { } on
            ? monitor with { Start = on.Start, Centre = on.Centre, End = on.End }
            : monitor;
    }

    /// <summary>Reads the panels again, where the session is Plasma's: once more after the reading under way, if one is.</summary>
    public async Task ReadAsync()
    {
        if (!_runs || _reading)
        {
            _owed = _reading;
            return;
        }

        _reading = true;
        try
        {
            using var said = await _bus.CallAsync(
                "org.kde.plasmashell", "/PlasmaShell", "org.kde.PlasmaShell", "evaluateScript", GLib.Variant.NewTuple([GLib.Variant.NewString(Script)]));
            await Callbacks.OnMainLoop();
            var panels = Parse(said.GetChildValue(0).GetString(out _));
            if (!panels.SequenceEqual(_panels))
            {
                _panels = panels;
                Changed();
            }
        }
        catch (GLib.GException ex)
        {
            await Callbacks.OnMainLoop();
            if (!_unreadSaid)
            {
                _unreadSaid = true;
                _log.PlasmaPanelsUnread(ex);
            }
        }
        finally
        {
            // Whatever else a reading throws, the next one still reads.
            _reading = false;
        }

        if (_owed)
        {
            _owed = false;
            await ReadAsync();
        }
    }

    /// <summary>
    /// The panels as the script prints them: on each, the start is what lies before the task manager, or before the tray
    /// with none, the centre the task manager, and the end the tray and what follows it, each from the first widget that
    /// takes room to the last. Nothing for what is not the script's JSON, as plasmashell's error.
    /// </summary>
    public static IReadOnlyList<PlasmaPanel> Parse(string printed)
    {
        try
        {
            using var json = JsonDocument.Parse(printed);
            return [.. json.RootElement.EnumerateArray().Select(PanelOf)];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return [];
        }
    }

    public void Dispose() => _again?.Dispose();

    private static PlasmaPanel PanelOf(JsonElement panel)
    {
        int Of(string name)
        {
            return (int)Math.Round(panel.GetProperty(name).GetDouble());
        }

        var x = Of("x");
        // A separator takes a pixel of room, or none, as the pager with one desktop.
        var widgets = panel.GetProperty("widgets").EnumerateArray()
            .Select(widget => (
                Type: widget.GetProperty("type").GetString() ?? "",
                Left: x + widget.GetProperty("x").GetDouble(),
                Width: widget.GetProperty("width").GetDouble()))
            .Where(widget => widget.Width > 1)
            .ToList();
        var tasks = widgets.FindIndex(widget => widget.Type is "org.kde.plasma.icontasks" or "org.kde.plasma.taskmanager");
        var tray = widgets.FindIndex(widget => widget.Type == "org.kde.plasma.systemtray");
        var start = widgets[..Math.Max(0, tasks >= 0 ? tasks : tray)];
        List<(string Type, double Left, double Width)> centre = tasks >= 0 ? [widgets[tasks]] : [];
        var end = tray >= 0 ? widgets[tray..] : [];
        static (double, double) Span(List<(string Type, double Left, double Width)> part)
        {
            return part.Count == 0 ? (0, 0) : (part.Min(widget => widget.Left), part.Max(widget => widget.Left + widget.Width));
        }

        var bottom = panel.GetProperty("location").GetString() == "bottom";
        return new PlasmaPanel((x, Of("y"), Of("width"), Of("height")), bottom, Span(start), Span(centre), Span(end));
    }
}

/// <summary>
/// A Plasma panel's monitor, by its place on the stage, whether it is along its bottom, and its start, centre and end
/// along it, (0, 0) for none.
/// </summary>
internal sealed record PlasmaPanel(
    (int X, int Y, int Width, int Height) Place,
    bool Bottom,
    (double Left, double Right) Start,
    (double Left, double Right) Centre,
    (double Left, double Right) End);
