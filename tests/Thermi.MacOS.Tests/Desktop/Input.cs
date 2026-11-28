using System.Globalization;

namespace Thermi.MacOS.Tests.Desktop;

/// <summary>
/// The pointer and the keyboard as a person moves them, in points from the top left of the main monitor: each step a
/// CoreGraphics event posted where the HID system posts its own, from osascript's JavaScript, which only a session made
/// for tests runs. Each call ends once its events are posted.
/// </summary>
internal static class Input
{
    /// <summary>Esc's key code, as a Mac's keyboard sends it, and the function keys' the hotkeys take.</summary>
    public const int Escape = 53;
    public const int F9 = 101;
    public const int F10 = 109;
    public const int F11 = 103;
    public const int F12 = 111;
    public const int Space = 49;

    /// <summary>CGEventFlags' Control and Option together, as a hotkey of Ctrl+Alt holds them, and ⌘.</summary>
    public const long ControlOption = 0x40000 | 0x80000;
    public const long Command = 0x100000;

    // Posts the events its arguments name, each "kind x y flags": a mouse event of CGEventType's number at a point, with
    // CGEventFlags' modifiers; "key code down flags", a key pressed or let go; "wheel x y lines flags", the wheel's notches;
    // "pinch x y amount phase flags", a step of a trackpad's pinch. No public API posts a pinch: it is a gesture event, of
    // type 29, whose magnification and phase sit in fields Apple does not document, as tools that post one use them.
    private const string Post = """
        ObjC.import('CoreGraphics');
        function run(argv) {
          for (const step of argv) {
            const [kind, ...n] = step.split(' ');
            const point = $.CGPointMake(Number(n[0]), Number(n[1]));
            const event = kind === 'key' ? $.CGEventCreateKeyboardEvent(null, Number(n[0]), n[1] === '1')
              : kind === 'wheel' ? $.CGEventCreateScrollWheelEvent(null, 1, 1, Number(n[2]))
              : kind === 'pinch' ? $.CGEventCreate(null)
              : $.CGEventCreateMouseEvent(null, Number(kind), point, kind === '3' || kind === '4' ? 1 : 0);
            if (kind === 'pinch') {
              $.CGEventSetType(event, 29);
              $.CGEventSetIntegerValueField(event, 110, 8);
              $.CGEventSetIntegerValueField(event, 132, Number(n[3]));
              $.CGEventSetDoubleValueField(event, 113, Number(n[2]));
            }
            if (kind === 'wheel' || kind === 'pinch') { $.CGEventSetLocation(event, point); }
            $.CGEventSetFlags(event, Number(n[n.length - 1]));
            $.CGEventPost(0, event);
          }
        }
        """;

    // CGEventType's mouse events, and CGEventFlags' modifiers.
    private const int LeftDown = 1;
    private const int LeftUp = 2;
    private const int RightDown = 3;
    private const int RightUp = 4;
    private const int Moved = 5;
    private const int LeftDragged = 6;
    private const long Control = 0x40000;

    public static Task MoveAsync(OwnSession session, int x, int y, CancellationToken cancel) => PostAsync(session, cancel, Mouse(Moved, x, y));

    /// <summary>A click of the left button, twice for a double-click.</summary>
    public static Task ClickAsync(OwnSession session, int x, int y, CancellationToken cancel, int times = 1) =>
        PostAsync(session, cancel, [.. Enumerable.Repeat<string[]>([Mouse(LeftDown, x, y), Mouse(LeftUp, x, y)], times).SelectMany(click => click)]);

    /// <summary>A right-click, or a left click with Control held, which macOS takes for one.</summary>
    public static Task MenuClickAsync(OwnSession session, int x, int y, bool control, CancellationToken cancel) => control
        ? PostAsync(session, cancel, Mouse(LeftDown, x, y, Control), Mouse(LeftUp, x, y, Control))
        : PostAsync(session, cancel, Mouse(RightDown, x, y), Mouse(RightUp, x, y));

    /// <summary>
    /// A press at <paramref name="from"/>, a move to <paramref name="to"/> in steps, and, unless <paramref name="letGo"/> is
    /// false, the button let go there.
    /// </summary>
    public static Task DragAsync(OwnSession session, (int X, int Y) from, (int X, int Y) to, CancellationToken cancel, bool letGo = true)
    {
        var steps = Enumerable.Range(1, 8)
            .Select(step => Mouse(LeftDragged, from.X + ((to.X - from.X) * step / 8), from.Y + ((to.Y - from.Y) * step / 8)));
        string[] end = letGo ? [Mouse(LeftUp, to.X, to.Y)] : [];
        return PostAsync(session, cancel, [Mouse(LeftDown, from.X, from.Y), .. steps, .. end]);
    }

    /// <summary>The left button let go where the pointer is, at the end of a drag <see cref="DragAsync"/> held.</summary>
    public static Task LetGoAsync(OwnSession session, int x, int y, CancellationToken cancel) => PostAsync(session, cancel, Mouse(LeftUp, x, y));

    /// <summary>A key pressed and let go, with <paramref name="modifiers"/> held, as CGEventFlags has them.</summary>
    public static Task KeyAsync(OwnSession session, int code, CancellationToken cancel, long modifiers = 0) =>
        PostAsync(session, cancel, Invariant($"key {code} 1 {modifiers}"), Invariant($"key {code} 0 {modifiers}"));

    /// <summary>Notches of the wheel with ⌘ held, up for more.</summary>
    public static Task CommandWheelAsync(OwnSession session, int x, int y, int notches, CancellationToken cancel) =>
        PostAsync(session, cancel, Invariant($"wheel {x} {y} {notches} {Command}"));

    /// <summary>A pinch on the trackpad, begun, spread by <paramref name="amount"/> of its size, and ended.</summary>
    public static Task PinchAsync(OwnSession session, int x, int y, double amount, CancellationToken cancel) =>
        PostAsync(session, cancel, Invariant($"pinch {x} {y} 0 1 0"), Invariant($"pinch {x} {y} {amount} 2 0"), Invariant($"pinch {x} {y} 0 4 0"));

    private static string Mouse(int kind, int x, int y, long flags = 0) => Invariant($"{kind} {x} {y} {flags}");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static async Task PostAsync(OwnSession session, CancellationToken cancel, params string[] steps)
    {
        var script = Path.Combine(session.Temporary, "input.js");
        if (!File.Exists(script))
        {
            await File.WriteAllTextAsync(script, Post, cancel).ConfigureAwait(false);
        }

        var posted = await session.RunAsync("osascript", ["-l", "JavaScript", script, .. steps], cancel: cancel).ConfigureAwait(false);
        if (posted.Exit != 0)
        {
            throw new InvalidOperationException($"osascript posted no input: {posted.Error}");
        }
    }
}
