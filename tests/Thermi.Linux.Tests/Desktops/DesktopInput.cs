using System.Globalization;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A person's pointer and keyboard on a test's desktop, through each desktop's own input for tests: GNOME Shell's virtual
/// devices, run in the Shell over its session bus; Sway's seat commands and a virtual keyboard; and XTEST on an X11 desktop
/// and in KWin's Xwayland. Points are the screenshots' pixels, which Mutter and Sway take as logical ones divided by
/// <paramref name="scale"/>.
/// </summary>
internal sealed class DesktopInput(DesktopKind kind, OwnSession session, double scale = 1)
{
    public async Task ClickAsync(int x, int y, CancellationToken cancel, int button = 1)
    {
        await MoveAsync(x, y, cancel).ConfigureAwait(false);
        await ButtonAsync(button, pressed: true, cancel).ConfigureAwait(false);
        await ButtonAsync(button, pressed: false, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Both clicks of a double-click in one go to the desktop, so they come as close together as a person's do, whatever
    /// the test's machine is busy with between its calls.
    /// </summary>
    public async Task DoubleClickAsync(int x, int y, CancellationToken cancel)
    {
        await MoveAsync(x, y, cancel).ConfigureAwait(false);
        string[] gnome = [.. Enumerable.Repeat(new[] { Button(1, pressed: true), Button(1, pressed: false) }, 2).SelectMany(clicks => clicks)];
        await RunAsync(
            string.Join("; ", gnome),
            ("swaymsg", ["seat - cursor press button1; seat - cursor release button1; seat - cursor press button1; seat - cursor release button1"]),
            ["click", "--repeat", "2", "1"],
            cancel).ConfigureAwait(false);
    }

    public Task DragAsync((int X, int Y) from, (int X, int Y) to, CancellationToken cancel) =>
        HoldAndDropAsync(from, to, _ => Task.CompletedTask, cancel);

    /// <summary>
    /// Presses the pointer at a point and holds it until <paramref name="held"/> ends, as a person waits for the pill to
    /// lift, then drops it at another: the test waits for what thermi says it did, armed before the press, never for a time.
    /// </summary>
    public Task HoldAndDropAsync((int X, int Y) from, (int X, int Y) to, Func<CancellationToken, Task> held, CancellationToken cancel) =>
        HoldAsync(from, to, held, _ => Task.CompletedTask, cancel);

    /// <summary>
    /// As <see cref="HoldAndDropAsync"/>, with <paramref name="there"/> run once the pointer reached <paramref name="to"/>,
    /// still pressed, as a person presses Esc before letting go.
    /// </summary>
    public async Task HoldAsync(
        (int X, int Y) from,
        (int X, int Y) to,
        Func<CancellationToken, Task> held,
        Func<CancellationToken, Task> there,
        CancellationToken cancel,
        int button = 1)
    {
        await MoveAsync(from.X, from.Y, cancel).ConfigureAwait(false);
        await ButtonAsync(button, pressed: true, cancel).ConfigureAwait(false);
        await held(cancel).ConfigureAwait(false);
        await MoveAsync((from.X + to.X) / 2, (from.Y + to.Y) / 2, cancel).ConfigureAwait(false);
        await MoveAsync(to.X, to.Y, cancel).ConfigureAwait(false);
        await there(cancel).ConfigureAwait(false);
        await ButtonAsync(button, pressed: false, cancel).ConfigureAwait(false);
    }

    /// <summary>The wheel turned <paramref name="notches"/> up over a point, with <paramref name="holding"/> held, as Control.</summary>
    public async Task WheelAsync(int x, int y, int notches, string? holding, CancellationToken cancel)
    {
        await MoveAsync(x, y, cancel).ConfigureAwait(false);
        await KeyAsync(holding, pressed: true, cancel).ConfigureAwait(false);
        for (var notch = 0; notch < Math.Abs(notches); notch++)
        {
            // X11's buttons 4 and 5 are the wheel's turns up and down, as Sway and XTEST read them; Mutter's virtual pointer
            // takes a wheel's turn as a discrete scroll of its own, and a button 4 as a button.
            var up = notches > 0;
            await RunAsync(
                $"pointer.notify_discrete_scroll(global.get_current_time(), Clutter.ScrollDirection.{(up ? "UP" : "DOWN")}, Clutter.ScrollSource.WHEEL)",
                ("swaymsg", [$"seat - cursor press button{(up ? 4 : 5)}; seat - cursor release button{(up ? 4 : 5)}"]),
                ["click", up ? "4" : "5"],
                cancel).ConfigureAwait(false);
        }

        await KeyAsync(holding, pressed: false, cancel).ConfigureAwait(false);
    }

    /// <summary>A key pressed and let go, by its X11 keysym's name, as Escape or Super_L, or keys held together, as Control_L+z.</summary>
    public async Task PressAsync(string key, CancellationToken cancel)
    {
        await KeyAsync(key, pressed: true, cancel).ConfigureAwait(false);
        await KeyAsync(key, pressed: false, cancel).ConfigureAwait(false);
    }

    public Task MoveAsync(int x, int y, CancellationToken cancel)
    {
        var (logicalX, logicalY) = (Logical(x), Logical(y));
        return RunAsync(
            $"pointer.notify_absolute_motion(global.get_current_time(), {logicalX}, {logicalY})",
            ("swaymsg", [$"seat - cursor set {logicalX} {logicalY}"]),
            ["mousemove", $"{x}", $"{y}"],
            cancel);
    }

    /// <summary>
    /// A line of the Shell's own JavaScript, run with the virtual pointer and keyboard of the Shell's seat as
    /// <c>pointer</c> and <c>keyboard</c>; the tests' Shell runs in its unsafe mode, which lets Eval in.
    /// </summary>
    public Task ShellAsync(string line, CancellationToken cancel) => RunAsync(Shell(line), cancel);

    private static string Button(int button, bool pressed) =>
        $"pointer.notify_button(global.get_current_time(), {button}, Clutter.ButtonState.{(pressed ? "PRESSED" : "RELEASED")})";

    private Task ButtonAsync(int button, bool pressed, CancellationToken cancel) => RunAsync(
        Button(button, pressed),
        ("swaymsg", [$"seat - cursor {(pressed ? "press" : "release")} button{button}"]),
        [pressed ? "mousedown" : "mouseup", $"{button}"],
        cancel);

    // xdotool takes keys held together as one; the Shell and wtype a key at a time, pressed in order and let go in reverse.
    private async Task KeyAsync(string? key, bool pressed, CancellationToken cancel)
    {
        if (key is null)
        {
            return;
        }

        var keys = key.Split('+');
        foreach (var each in pressed ? keys : keys.Reverse())
        {
            await RunAsync(
                $"keyboard.notify_keyval(global.get_current_time(), Clutter.KEY_{each}, Clutter.KeyState.{(pressed ? "PRESSED" : "RELEASED")})",
                ("wtype", [pressed ? "-P" : "-p", each]),
                [pressed ? "keydown" : "keyup", key],
                cancel).ConfigureAwait(false);
            if (kind is DesktopKind.KWin or DesktopKind.X11)
            {
                break;
            }
        }
    }

    private string Logical(int pixels) =>
        kind is DesktopKind.Gnome or DesktopKind.Sway ? (pixels / scale).ToString("0.##", CultureInfo.InvariantCulture) : $"{pixels}";

    // The Shell's Eval runs in a module that imports no Clutter, so the line takes GJS's own.
    private static (string, string[]) Shell(string line)
    {
        var script = "const Clutter = imports.gi.Clutter;"
            + "const seat = Clutter.get_default_backend().get_default_seat();"
            + "const pointer = global.thermiPointer ??= seat.create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);"
            + "const keyboard = global.thermiKeyboard ??= seat.create_virtual_device(Clutter.InputDeviceType.KEYBOARD_DEVICE);"
            + line;
        return ("gdbus", TestDesktop.Call("org.gnome.Shell", "/org/gnome/Shell", "Eval", script));
    }

    // The same input as each desktop takes it: a line of the Shell's, Sway's own command, or xdotool's XTEST.
    private Task RunAsync(string shell, (string, string[]) sway, string[] xtest, CancellationToken cancel) => RunAsync(
        kind switch
        {
            DesktopKind.Gnome or DesktopKind.GnomeX11 => Shell(shell),
            DesktopKind.Sway => sway,
            DesktopKind.KWin or DesktopKind.X11 => ("xdotool", xtest),
            DesktopKind.BareWayland => throw new NotSupportedException("Weston's kiosk shell takes no input for tests."),
            _ => throw new InvalidOperationException($"Unknown desktop {kind}."),
        },
        cancel);

    // The Shell's Eval answers (false, 'why') when the line threw, and gdbus still exits 0.
    private async Task RunAsync((string Program, string[] Args) command, CancellationToken cancel)
    {
        var ran = await session.RunAsync(command.Program, command.Args, cancel: cancel).ConfigureAwait(false);
        var threw = command.Program == "gdbus" && !ran.Out.TrimStart().StartsWith("(true", StringComparison.Ordinal);
        if (ran.Exit != 0 || threw)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"{kind} took no input from {command.Program}: {ran.Error}{ran.Out}"));
        }
    }
}
