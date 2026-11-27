using System.Globalization;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// A person's pointer and keyboard on a test's desktop, through each desktop's own input for tests: GNOME Shell's virtual
/// devices, run in the Shell over its session bus; Sway's seat commands and a virtual keyboard; and XTEST on an X11 desktop
/// and in KWin's Xwayland.
/// </summary>
internal sealed class DesktopInput(DesktopKind kind, OwnSession session)
{
    public async Task ClickAsync(int x, int y, CancellationToken cancel, int button = 1)
    {
        await MoveAsync(x, y, cancel).ConfigureAwait(false);
        await ButtonAsync(button, pressed: true, cancel).ConfigureAwait(false);
        await ButtonAsync(button, pressed: false, cancel).ConfigureAwait(false);
    }

    public Task DragAsync((int X, int Y) from, (int X, int Y) to, CancellationToken cancel) =>
        HoldAndDropAsync(from, to, _ => Task.CompletedTask, cancel);

    /// <summary>
    /// Presses the pointer at a point and holds it until <paramref name="lifted"/> sees the pill lifted, as a person waits
    /// for it, then drops it at another: the test waits for what the overlay shows, never for a time.
    /// </summary>
    public async Task HoldAndDropAsync((int X, int Y) from, (int X, int Y) to, Func<CancellationToken, Task> lifted, CancellationToken cancel)
    {
        await MoveAsync(from.X, from.Y, cancel).ConfigureAwait(false);
        await ButtonAsync(1, pressed: true, cancel).ConfigureAwait(false);
        await lifted(cancel).ConfigureAwait(false);
        await MoveAsync((from.X + to.X) / 2, (from.Y + to.Y) / 2, cancel).ConfigureAwait(false);
        await MoveAsync(to.X, to.Y, cancel).ConfigureAwait(false);
        await ButtonAsync(1, pressed: false, cancel).ConfigureAwait(false);
    }

    /// <summary>The wheel turned <paramref name="notches"/> up over a point, with <paramref name="holding"/> held, as Control.</summary>
    public async Task WheelAsync(int x, int y, int notches, string? holding, CancellationToken cancel)
    {
        await MoveAsync(x, y, cancel).ConfigureAwait(false);
        await KeyAsync(holding, pressed: true, cancel).ConfigureAwait(false);
        for (var notch = 0; notch < Math.Abs(notches); notch++)
        {
            // X11's buttons 4 and 5 are the wheel's turns up and down, as every desktop here reads them.
            await ButtonAsync(notches > 0 ? 4 : 5, pressed: true, cancel).ConfigureAwait(false);
            await ButtonAsync(notches > 0 ? 4 : 5, pressed: false, cancel).ConfigureAwait(false);
        }

        await KeyAsync(holding, pressed: false, cancel).ConfigureAwait(false);
    }

    /// <summary>A key pressed and let go, by its X11 keysym's name, as Escape or Super_L.</summary>
    public async Task PressAsync(string key, CancellationToken cancel)
    {
        await KeyAsync(key, pressed: true, cancel).ConfigureAwait(false);
        await KeyAsync(key, pressed: false, cancel).ConfigureAwait(false);
    }

    public Task MoveAsync(int x, int y, CancellationToken cancel) => RunAsync(
        $"pointer.notify_absolute_motion(global.get_current_time(), {x}, {y})",
        ("swaymsg", [$"seat - cursor set {x} {y}"]),
        ["mousemove", $"{x}", $"{y}"],
        cancel);

    /// <summary>
    /// A line of the Shell's own JavaScript, run with the virtual pointer and keyboard of the Shell's seat as
    /// <c>pointer</c> and <c>keyboard</c>; the tests' Shell runs in its unsafe mode, which lets Eval in.
    /// </summary>
    public Task ShellAsync(string line, CancellationToken cancel) => RunAsync(Shell(line), cancel);

    private Task ButtonAsync(int button, bool pressed, CancellationToken cancel) => RunAsync(
        $"pointer.notify_button(global.get_current_time(), {button}, Clutter.ButtonState.{(pressed ? "PRESSED" : "RELEASED")})",
        ("swaymsg", [$"seat - cursor {(pressed ? "press" : "release")} button{button}"]),
        [pressed ? "mousedown" : "mouseup", $"{button}"],
        cancel);

    private Task KeyAsync(string? key, bool pressed, CancellationToken cancel) => key is null ? Task.CompletedTask : RunAsync(
        $"keyboard.notify_keyval(global.get_current_time(), Clutter.KEY_{key}, Clutter.KeyState.{(pressed ? "PRESSED" : "RELEASED")})",
        ("wtype", [pressed ? "-P" : "-p", key]),
        [pressed ? "keydown" : "keyup", key],
        cancel);

    private static (string, string[]) Shell(string line)
    {
        var script = "const seat = Clutter.get_default_backend().get_default_seat();"
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

    private async Task RunAsync((string Program, string[] Args) command, CancellationToken cancel)
    {
        var ran = await session.RunAsync(command.Program, command.Args, cancel: cancel).ConfigureAwait(false);
        if (ran.Exit != 0)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{kind} took no input from {command.Program}: {ran.Error}"));
        }
    }
}
