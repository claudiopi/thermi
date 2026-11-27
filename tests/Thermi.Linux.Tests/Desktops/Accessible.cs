using System.Globalization;
using System.Text.RegularExpressions;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// The desktop's accessibility tree, as a screen reader reads it: the menus open on screen, by the program that shows
/// them, and their entries. Both the Shell's own menus and GTK's are in it, so a test reads either the same way: a menu's
/// entries are the items it shows, at any depth, as the Shell nests them in a box of its own; an entry with no action,
/// as the Shell's, is clicked where it shows.
/// </summary>
internal static class Accessible
{
    // AT-SPI from Python: each open menu's program and the number of its entries, one menu a line, the entry at an index of
    // the open menu chosen, each program's windows, or where the first knob of thermi's window is in it.
    private const string Code = """
        import sys, gi
        gi.require_version('Atspi', '2.0')
        from gi.repository import Atspi, Gio, GLib

        # GNOME Shell tells of a menu that shows only once accessibility is on; reading the tree does not turn it on.
        Gio.bus_get_sync(Gio.BusType.SESSION, None).call_sync(
            'org.a11y.Bus', '/org/a11y/bus', 'org.freedesktop.DBus.Properties', 'Set',
            GLib.Variant('(ssv)', ('org.a11y.Status', 'IsEnabled', GLib.Variant('b', True))), None, Gio.DBusCallFlags.NONE, -1, None)

        ITEMS = (Atspi.Role.MENU_ITEM, Atspi.Role.CHECK_MENU_ITEM, Atspi.Role.RADIO_MENU_ITEM, Atspi.Role.MENU)

        def showing(node):
            return node.get_state_set().contains(Atspi.StateType.SHOWING)

        def entries(node):
            for index in range(node.get_child_count()):
                child = node.get_child_at_index(index)
                if child is None or not showing(child): continue
                if child.get_role() in ITEMS: yield child
                else: yield from entries(child)

        def menus(node, app):
            if node is None: return
            if node.get_role() in (Atspi.Role.MENU, Atspi.Role.POPUP_MENU) and showing(node):
                shown = list(entries(node))
                if shown: yield app, shown
                return
            for index in range(node.get_child_count()):
                yield from menus(node.get_child_at_index(index), app)

        desktop = Atspi.get_desktop(0)
        def open_menus():
            programs = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
            return [menu for program in programs if program is not None for menu in menus(program, program.get_name())]

        found = open_menus()
        if sys.argv[1] in ('list', 'choose') and not found:
            def heard(event):
                if open_menus(): Atspi.event_quit()
            listener = Atspi.EventListener.new(heard)
            for kind in ('object:state-changed:showing', 'object:children-changed', 'window:activate'):
                listener.register(kind)
            found = open_menus()
            if not found:
                Atspi.event_main()
                found = open_menus()
        # A line's knob: the chart's plot is a slider too, an svg's, whose drag reads a point and changes nothing.
        def sliders(node):
            if node is None: return
            if node.get_role() == Atspi.Role.SLIDER and node.get_attributes().get('tag') != 'svg': yield node
            for index in range(node.get_child_count()):
                yield from sliders(node.get_child_at_index(index))

        apps = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
        if sys.argv[1] == 'list':
            for app, shown in found: print(app, len(shown))
        elif sys.argv[1] == 'windows':
            for app in apps:
                frames = sum(1 for index in range(app.get_child_count()) if app.get_child_at_index(index).get_role() == Atspi.Role.FRAME)
                if frames: print(app.get_process_id(), frames, app.get_name())
        elif sys.argv[1] == 'slider':
            box = next(slider for app in apps if app.get_name() == 'thermi' for slider in sliders(app)).get_extents(Atspi.CoordType.WINDOW)
            print(box.x + box.width // 2, box.y + box.height // 2)
        else:
            entry = found[0][1][int(sys.argv[2])]
            action = entry.get_action_iface()
            if action is not None and action.get_n_actions() > 0:
                action.do_action(0)
            else:
                box = entry.get_extents(Atspi.CoordType.SCREEN)
                print('click', box.x + box.width // 2, box.y + box.height // 2)
        """;

    /// <summary>Each menu open on screen, once one is: the program that shows it, and how many entries it holds.</summary>
    public static async Task<IReadOnlyList<(string Program, int Entries)>> OpenMenusAsync(OwnSession session, CancellationToken cancel)
    {
        var said = await RunAsync(session, ["list"], cancel).ConfigureAwait(false);
        return [.. said.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' '))
            .Select(words => (string.Join(' ', words[..^1]), int.Parse(words[^1], CultureInfo.InvariantCulture)))];
    }

    /// <summary>Chooses the entry at <paramref name="index"/> of the menu open on screen, as a person clicks it.</summary>
    public static async Task ChooseAsync(OwnSession session, int index, CancellationToken cancel)
    {
        var said = await RunAsync(session, ["choose", index.ToString(CultureInfo.InvariantCulture)], cancel).ConfigureAwait(false);
        _ = said.Length == 0 ? 0 : throw new InvalidOperationException($"The menu's entry {index} has no action: {said}");
    }

    /// <summary>
    /// Chooses the entry at <paramref name="index"/> of the menu open on <paramref name="desktop"/>, by its action, or by a
    /// click where it shows, in the tree's logical pixels.
    /// </summary>
    public static async Task ChooseAsync(TestDesktop desktop, int index, CancellationToken cancel)
    {
        var said = await RunAsync(desktop.Session, ["choose", index.ToString(CultureInfo.InvariantCulture)], cancel).ConfigureAwait(false);
        if (said.Split(' ', StringSplitOptions.TrimEntries) is ["click", var x, var y])
        {
            var (atX, atY) = (double.Parse(x, CultureInfo.InvariantCulture), double.Parse(y, CultureInfo.InvariantCulture));
            await desktop.Input.ClickAsync((int)Math.Round(atX * desktop.Scale), (int)Math.Round(atY * desktop.Scale), cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Each program that shows windows: its process, and how many windows it shows.</summary>
    public static async Task<IReadOnlyList<(int Process, int Windows, string Program)>> WindowsAsync(OwnSession session, CancellationToken cancel)
    {
        var said = await RunAsync(session, ["windows"], cancel).ConfigureAwait(false);
        return [.. said.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ', 3))
            .Select(words => (int.Parse(words[0], CultureInfo.InvariantCulture), int.Parse(words[1], CultureInfo.InvariantCulture), words[2]))];
    }

    /// <summary>
    /// Drags the first knob of a line of thermi's window on GNOME <paramref name="by"/> pixels down, as a person does, and returns
    /// when it started to move, in seconds of the system's monotonic clock, as thermi's trace says its times. WebKit takes no
    /// value set through AT-SPI on the page's sliders, and gives their place in its window, as Wayland tells no program where
    /// its window is: the Shell's frame of the window says where.
    /// </summary>
    public static async Task<double> SlideAsync(TestDesktop desktop, int by, CancellationToken cancel)
    {
        var inWindow = Numbers(await RunAsync(desktop.Session, ["slider"], cancel).ConfigureAwait(false));
        var frame = TestDesktop.Call(
            "org.gnome.Shell", "/org/gnome/Shell", "Eval",
            "const f = global.get_window_actors().map(a => a.meta_window).find(w => w.get_title() === 'thermi').get_frame_rect(); [f.x, f.y].join(' ')");
        var window = Numbers((await desktop.Session.RunAsync("gdbus", frame, cancel: cancel).ConfigureAwait(false)).Out);
        var (x, y) = ((int)Math.Round((window[0] + inWindow[0]) * desktop.Scale), (int)Math.Round((window[1] + inWindow[1]) * desktop.Scale));
        var moved = 0.0;
        await desktop.Input.HoldAndDropAsync(
            (x, y),
            (x, y + by),
            _ =>
            {
                moved = TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency;
                return Task.CompletedTask;
            },
            cancel).ConfigureAwait(false);
        return moved;
    }

    private static double[] Numbers(string said) =>
        [.. Regex.Matches(said, @"-?\d+(\.\d+)?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Select(number => double.Parse(number.Value, CultureInfo.InvariantCulture))];

    private static async Task<string> RunAsync(OwnSession session, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "accessible.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var ran = await session.RunAsync("python3", [script, .. args], cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? ran.Out : throw new InvalidOperationException($"The accessibility tree did not answer: {ran.Error}");
    }
}
