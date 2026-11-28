using Thermi.Core.Messages;
using Thermi.Linux.Agents;
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

    /// <summary>The tests' own program, started in a session with it, tells the overlay messages as the window does (<see cref="SendAsTheWindow"/>).</summary>
    public const string Sends = "--overlay-sends";

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

    // Owns the overlay's name and hears the window's messages on its object, as the overlay does; once it heard as many as
    // it is told, it says each, in the order heard, and ends.
    private const string Hearer = """
        import sys, gi
        from gi.repository import Gio, GLib
        XML = "<node><interface name='io.github.claudiopi.thermi.Overlay'><method name='Message'><arg type='s'/></method></interface></node>"
        loop = GLib.MainLoop()
        heard = []
        def call(bus, sender, path, face, method, args, invocation):
            heard.append(args.unpack()[0])
            invocation.return_value(None)
            if len(heard) == int(sys.argv[2]):
                print('\n'.join(heard), flush=True)
                loop.quit()
        def bus_up(bus, name):
            bus.register_object('/io/github/claudiopi/thermi/Overlay', Gio.DBusNodeInfo.new_for_xml(XML).interfaces[0], call)
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
    // session's target for systemd, as Sway and Hyprland may not, the overlay as a unit of its own, never both; either
    // comes back if it fails, and a second Start finds it running. Exit thermi ends the overlay it finds: the stand-in gives
    // its name up only by ending.
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

        var started = await LinkAsync(session, "start", cancel, overlay) && await LinkAsync(session, "start", cancel, overlay);
        string[] both = [UserUnit.Name, "thermi-overlay.service"];
        var ran = new List<string>();
        foreach (var unit in both)
        {
            if (await units.SystemctlAsync(["is-active", unit], cancel) == "active")
            {
                ran.Add(unit);
            }
        }

        var restarts = await units.SystemctlAsync(["show", "-p", "Restart", "--value", .. ran], cancel);
        var ended = !await LinkAsync(session, "quit", cancel);

        Assert.Equal((true, both[graphical ? 0 : 1], "on-failure", true), (started, string.Join(' ', ran), restarts, ended));
    }

    // What the window tells the overlay reaches it in the order told, as an item pointed and then none, which leave no mark.
    [Fact(Timeout = Bound.Test)]
    public async Task TheOverlayHearsTheWindowInOrder()
    {
        const int Messages = 500;
        var cancel = TestContext.Current.CancellationToken;
        await using var session = new OwnSession();
        var script = Path.Combine(session.Home, "hearer.py");
        await File.WriteAllTextAsync(script, Hearer, cancel);
        var hearer = await session.StartAsync("python3", [script, ShellLink.Name, $"{Messages}"], cancel: cancel);
        await hearer.ReadyAsync(cancel);

        var sent = await session.RunAsync(Built.Program("Thermi.Linux.Tests"), [Sends, $"{Messages}"], cancel: cancel);
        var heard = await hearer.Out.ToEndAsync(cancel);

        Assert.Equal(0, sent.Exit);
        // As Core encodes each, which the overlay hears in base64.
        Assert.Equal(Told(Messages).Select(message => Convert.ToBase64String(WindowMessages.Encode(message))), heard.Split('\n').Skip(1));
    }

    /// <summary>
    /// The tests' own program as <see cref="TheOverlayHearsTheWindowInOrder"/> starts it: the window's link, telling the
    /// overlay <paramref name="messages"/> messages at once, which ends once the overlay heard them.
    /// </summary>
    public static int SendAsTheWindow(int messages)
    {
        Gio.Module.Initialize();
        using var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        var context = GLib.MainContext.Default();
        using var link = new OverlayLink(bus, () => { });
        foreach (var message in Told(messages))
        {
            link.Send(message);
        }

        // The overlay's stand-in gives its name up once it heard them all.
        while (link.Running)
        {
            context.Iteration(true);
        }

        return 0;
    }

    // Each an item of its own pointed on an overlay.
    private static IEnumerable<Edited> Told(int messages) => Enumerable.Range(0, messages).Select(at => new Edited(0, null, $"item-{at}"));

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
        if (what == "start" && !await LinuxFixes.StartOverlay(thermi ?? throw new ArgumentNullException(nameof(thermi))).ConfigureAwait(false))
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
