using System.Globalization;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// The desktop's accessibility tree, as a screen reader reads it: the menus open on screen, by the program that shows
/// them, and their entries' actions. Both the Shell's own menus and GTK's are in it, so a test reads either the same way.
/// </summary>
internal static class Accessible
{
    // AT-SPI from Python: each open menu's program and the number of its entries, one menu a line, or the entry at an index
    // of the open menu chosen.
    private const string Code = """
        import sys, time, gi
        gi.require_version('Atspi', '2.0')
        from gi.repository import Atspi

        def menus(node, app):
            if node is None: return
            if node.get_role() in (Atspi.Role.MENU, Atspi.Role.POPUP_MENU) and node.get_state_set().contains(Atspi.StateType.SHOWING):
                yield app, node
            for index in range(node.get_child_count()):
                yield from menus(node.get_child_at_index(index), app)

        desktop = Atspi.get_desktop(0)
        programs = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
        found = [menu for program in programs for menu in menus(program, program.get_name())]
        def sliders(node):
            if node is None: return
            if node.get_role() == Atspi.Role.SLIDER: yield node
            for index in range(node.get_child_count()):
                yield from sliders(node.get_child_at_index(index))

        apps = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
        if sys.argv[1] == 'list':
            for app, menu in found: print(app, menu.get_child_count())
        elif sys.argv[1] == 'windows':
            for app in apps:
                frames = sum(1 for index in range(app.get_child_count()) if app.get_child_at_index(index).get_role() == Atspi.Role.FRAME)
                if frames: print(app.get_process_id(), frames, app.get_name())
        elif sys.argv[1] == 'slide':
            value = next(slider for app in apps if app.get_name() == 'thermi' for slider in sliders(app)).get_value_iface()
            moved = value.get_current_value() + float(sys.argv[2])
            print(time.monotonic(), flush=True)
            value.set_current_value(moved)
        else:
            entry = found[0][1].get_child_at_index(int(sys.argv[2]))
            entry.get_action_iface().do_action(0)
        """;

    /// <summary>Each menu open on screen: the program that shows it, and how many entries it holds.</summary>
    public static async Task<IReadOnlyList<(string Program, int Entries)>> OpenMenusAsync(OwnSession session, CancellationToken cancel)
    {
        var said = await RunAsync(session, ["list"], cancel).ConfigureAwait(false);
        return [.. said.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' '))
            .Select(words => (string.Join(' ', words[..^1]), int.Parse(words[^1], CultureInfo.InvariantCulture)))];
    }

    /// <summary>Chooses the entry at <paramref name="index"/> of the menu open on screen, as a person clicks it.</summary>
    public static Task ChooseAsync(OwnSession session, int index, CancellationToken cancel) =>
        RunAsync(session, ["choose", index.ToString(CultureInfo.InvariantCulture)], cancel);

    /// <summary>Each program that shows windows: its process, and how many windows it shows.</summary>
    public static async Task<IReadOnlyList<(int Process, int Windows, string Program)>> WindowsAsync(OwnSession session, CancellationToken cancel)
    {
        var said = await RunAsync(session, ["windows"], cancel).ConfigureAwait(false);
        return [.. said.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ', 3))
            .Select(words => (int.Parse(words[0], CultureInfo.InvariantCulture), int.Parse(words[1], CultureInfo.InvariantCulture), words[2]))];
    }

    /// <summary>
    /// Moves the first slider of thermi's window by <paramref name="by"/>, as a person drags it, and returns when it did, in
    /// seconds of the system's monotonic clock, as thermi's trace says its times.
    /// </summary>
    public static async Task<double> SlideAsync(OwnSession session, double by, CancellationToken cancel) =>
        double.Parse(await RunAsync(session, ["slide", by.ToString(CultureInfo.InvariantCulture)], cancel).ConfigureAwait(false), CultureInfo.InvariantCulture);

    private static async Task<string> RunAsync(OwnSession session, string[] args, CancellationToken cancel)
    {
        var script = Path.Combine(session.Home, "accessible.py");
        await File.WriteAllTextAsync(script, Code, cancel).ConfigureAwait(false);
        var ran = await session.RunAsync("python3", [script, .. args], cancel: cancel).ConfigureAwait(false);
        return ran.Exit == 0 ? ran.Out : throw new InvalidOperationException($"The accessibility tree did not answer: {ran.Error}");
    }
}
