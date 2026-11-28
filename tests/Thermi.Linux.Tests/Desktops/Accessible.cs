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
    // the open menu chosen, how many of its entries reach past its edges, each program's windows, where the first knob of
    // thermi's window is in it, or a line once the window of a title is the active one.
    private const string Code = """
        import sys, gi
        gi.require_version('Atspi', '2.0')
        from gi.repository import Atspi, Gio, GLib

        # GNOME Shell tells of a menu that shows only once accessibility is on; reading the tree does not turn it on.
        Gio.bus_get_sync(Gio.BusType.SESSION, None).call_sync(
            'org.a11y.Bus', '/org/a11y/bus', 'org.freedesktop.DBus.Properties', 'Set',
            GLib.Variant('(ssv)', ('org.a11y.Status', 'IsEnabled', GLib.Variant('b', True))), None, Gio.DBusCallFlags.NONE, -1, None)

        ITEMS = (Atspi.Role.MENU_ITEM, Atspi.Role.CHECK_MENU_ITEM, Atspi.Role.RADIO_MENU_ITEM, Atspi.Role.MENU)

        # A node its program let go while the tree is read, as a page's that changed, answers no more: it shows no longer.
        def alive(read, otherwise):
            try: return read()
            except GLib.GError: return otherwise

        def children(node):
            for index in range(alive(node.get_child_count, 0)):
                child = alive(lambda: node.get_child_at_index(index), None)
                if child is not None: yield child

        def role(node):
            return alive(node.get_role, None)

        def has(node, state):
            return alive(lambda: node.get_state_set().contains(state), False)

        def entries(node):
            for child in children(node):
                if not has(child, Atspi.StateType.SHOWING): continue
                if role(child) in ITEMS: yield child
                else: yield from entries(child)

        def menus(node, app):
            if role(node) in (Atspi.Role.MENU, Atspi.Role.POPUP_MENU) and has(node, Atspi.StateType.SHOWING):
                shown = list(entries(node))
                if shown: yield app, shown, node
                return
            for child in children(node):
                yield from menus(child, app)

        desktop = Atspi.get_desktop(0)
        def open_menus():
            return [menu for program in children(desktop) for menu in menus(program, alive(program.get_name, None))]

        # Only what asks of menus looks for them: the search reads every node of every program, the thousands of a page's
        # among them, which a busy PC takes minutes to answer.
        found = open_menus() if sys.argv[1] in ('closed', 'list', 'choose', 'cut') else []
        if sys.argv[1] == 'closed' and found:
            def gone(event):
                if not open_menus(): Atspi.event_quit()
            listener = Atspi.EventListener.new(gone)
            for kind in ('object:state-changed:showing', 'object:children-changed', 'window:deactivate'):
                listener.register(kind)
            if open_menus(): Atspi.event_main()
        if sys.argv[1] in ('list', 'choose', 'cut') and not found:
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
            if role(node) == Atspi.Role.SLIDER and alive(node.get_attributes, {}).get('tag') != 'svg': yield node
            for child in children(node):
                yield from sliders(child)

        apps = list(children(desktop))
        if sys.argv[1] == 'list':
            for app, shown, _ in found: print(app, len(shown))
        elif sys.argv[1] == 'cut':
            _, shown, menu = found[0]
            box = menu.get_extents(Atspi.CoordType.WINDOW)
            def cut(entry):
                each = entry.get_extents(Atspi.CoordType.WINDOW)
                return each.y < box.y or each.y + each.height > box.y + box.height
            print(sum(1 for entry in shown if cut(entry)))
        elif sys.argv[1] == 'windows':
            for app in apps:
                frames = sum(1 for child in children(app) if role(child) == Atspi.Role.FRAME)
                said = alive(lambda: (app.get_process_id(), app.get_name()), None)
                if frames and said: print(said[0], frames, said[1])
        elif sys.argv[1] == 'active':
            def frames():
                for app in children(desktop):
                    yield from (frame for frame in children(app) if role(frame) == Atspi.Role.FRAME)
            # Read each quarter second until it is, each read that differs from the last on standard error, so a wait that
            # runs out says what the desktop's windows were at its end, and whether each was the active one; an event
            # missed, or a read a busy program failed, is read again.
            said = []
            def active():
                read = [(alive(frame.get_name, None), has(frame, Atspi.StateType.ACTIVE)) for frame in frames()]
                if read != said:
                    said[:] = read
                    print(' | '.join(f'{name} {"active" if on else "inactive"}' for name, on in read), file=sys.stderr, flush=True)
                return (sys.argv[2], True) in read
            loop = GLib.MainLoop()
            def again():
                if active(): loop.quit()
                return True
            if not active():
                GLib.timeout_add(250, again)
                loop.run()
            print('active', flush=True)
        elif sys.argv[1] == 'slider':
            box = next(slider for app in apps if app.get_name() == 'thermi' for slider in sliders(app)).get_extents(Atspi.CoordType.WINDOW)
            print(box.x + box.width // 2, box.y + box.height // 2)
        elif sys.argv[1] == 'choose':
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

    /// <summary>How many entries of the menu open on screen reach past its top or its bottom, cut off or scrolled away.</summary>
    public static async Task<int> CutEntriesAsync(OwnSession session, CancellationToken cancel) =>
        int.Parse(await RunAsync(session, ["cut"], cancel).ConfigureAwait(false), CultureInfo.InvariantCulture);

    /// <summary>Done once no menu is open on screen.</summary>
    public static Task ClosedAsync(OwnSession session, CancellationToken cancel) => RunAsync(session, ["closed"], cancel);

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
    /// Returns once the window titled <paramref name="title"/> is the active one, the one the keys go to, as the program
    /// itself hears it from the desktop.
    /// </summary>
    public static async Task ActiveAsync(OwnSession session, string title, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "accessible.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var waiting = await session.StartAsync("python3", [script, "active", title], cancel: cancel).ConfigureAwait(false);
        _ = await waiting.ReadyAsync(cancel).ConfigureAwait(false);
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
        [.. Regex.Matches(said, @"-?\d+(\.\d+)?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1))
            .Select(number => double.Parse(number.Value, CultureInfo.InvariantCulture))];

    private static async Task<string> RunAsync(OwnSession session, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "accessible.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var ran = await session.RunAsync("python3", [script, .. args], cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? ran.Out : throw new InvalidOperationException($"The accessibility tree did not answer: {ran.Error}");
    }
}
