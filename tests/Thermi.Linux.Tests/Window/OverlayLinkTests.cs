using Thermi.Linux.Gnome;
using Thermi.Linux.Tests.Desktops;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Window;

/// <summary>
/// The overlay's process as the window and thermi-cli see it: running while it owns its name on the session's bus, started
/// as its user unit, and ended through the <c>quit</c> it exports there. An overlay of the test's own stands in for it.
/// </summary>
public sealed class OverlayLinkTests
{
    /// <summary>The tests' own program, started in a session with it, acts as the window's link (<see cref="AnswerAsTheWindowAsync"/>).</summary>
    public const string Linked = "--overlay-link";

    // Owns the name it is given and exports the overlay's quit there, as GApplication does, which ends it; says when it holds it.
    private const string Overlay = """
        import sys, gi
        from gi.repository import Gio, GLib
        loop = GLib.MainLoop()
        def bus_up(bus, name):
            actions = Gio.SimpleActionGroup()
            quit = Gio.SimpleAction.new('quit', None)
            quit.connect('activate', lambda *_: loop.quit())
            actions.add_action(quit)
            bus.export_action_group('/io/github/claudiopi/thermi', actions)
        Gio.bus_own_name(Gio.BusType.SESSION, sys.argv[1], Gio.BusNameOwnerFlags.DO_NOT_QUEUE, bus_up,
                         lambda *_: print('ready', flush=True), lambda *_: sys.exit('The name is taken.'))
        loop.run()
        """;

    // The overlay runs while it owns its own name, whatever else of thermi's runs: the window owns GTK's id, which is no
    // overlay. thermi-cli's status and the window's banner say the same.
    [Theory(Timeout = Bound.Test)] // Rows: the overlay with the window closed, and the window alone.
    [InlineData(ShellLink.Name, true)]
    [InlineData(WindowProcess.Id, false)]
    public async Task TheOverlayRunsWhileItOwnsItsName(string owned, bool running)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var script = Path.Combine(session.Home, "overlay.py");
        await File.WriteAllTextAsync(script, Overlay, cancel);
        var holder = await session.StartAsync("python3", [script, owned], cancel: cancel);
        await holder.ReadyAsync(cancel);

        var (_, cli) = await session.SdkStatusAsync(cancel);
        var window = await LinkAsync(session, "running", cancel);

        Assert.Equal((running, running), (cli, window));
    }

    // The window's Start starts the overlay's user unit, as a sign-in does, or, on a desktop that starts no graphical
    // session's target for systemd, as Sway and Hyprland may not, the overlay as a unit of its own; and Exit thermi ends the
    // overlay it finds: the stand-in gives its name up only by ending.
    [Theory(Timeout = Bound.Test)] // Rows: the graphical session's target up, and down.
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheWindowStartsAndEndsTheOverlay(bool graphical)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var script = Path.Combine(session.Home, "overlay.py");
        await File.WriteAllTextAsync(script, Overlay, cancel);
        var overlay = Path.Combine(session.Home, "overlay");
        await File.WriteAllTextAsync(overlay, $"#!/bin/sh\nexec python3 {script} {ShellLink.Name}\n", cancel);
        File.SetUnixFileMode(overlay, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var units = await UserUnits.StartAsync(session, cancel, overlay);
        if (!graphical)
        {
            await units.SystemctlAsync(["stop", "graphical-session.target"], cancel);
        }

        var started = await LinkAsync(session, "start", cancel, overlay);
        var ended = !await LinkAsync(session, "quit", cancel);

        Assert.Equal((true, true), (started, ended));
    }

    /// <summary>
    /// The tests' own program as these tests start it: the window's link to the overlay, which says whether the overlay runs,
    /// or starts it, as <paramref name="thermi"/> where its unit cannot start, and says once it runs, or ends it and says once
    /// it ended.
    /// </summary>
    public static async Task<int> AnswerAsTheWindowAsync(string what, string? thermi = null)
    {
        Gio.Module.Initialize();
        using var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        var context = GLib.MainContext.Default();
        using var link = new OverlayLink(bus, () => { });
        if (what == "start" && !await LinuxFixes.StartOverlay(thermi).ConfigureAwait(false))
        {
            return 1;
        }

        if (what == "quit")
        {
            link.Quit();
        }

        // The owner the proxy keeps changes on GLib's loop, as on the window's.
        var wanted = what switch { "start" => true, "quit" => false, _ => link.Running };
        while (link.Running != wanted)
        {
            context.Iteration(true);
        }

        Console.WriteLine(link.Running);
        return 0;
    }

    private static async Task<bool> LinkAsync(OwnSession session, string what, CancellationToken cancel, string? thermi = null)
    {
        var said = await session.RunAsync(Built.Program("Thermi.Linux.Tests"), [Linked, what, .. thermi is null ? [] : new[] { thermi }], cancel: cancel);
        return said.Exit == 0 && bool.TryParse(said.Out.Trim(), out var running)
            ? running
            : throw new InvalidOperationException($"The window's link did not answer: {said.Error}");
    }
}
