using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>What the running overlay does for its own programs on the bus: each on the GLib main loop's thread.</summary>
/// <param name="HotkeyStatus">Which hotkeys the desktop refused, as <see cref="Core.Hotkeys.Status"/> says it.</param>
/// <param name="State">What it tells agents.</param>
/// <param name="Heard">A message of the settings window's, Core's payload in base64.</param>
/// <param name="Open">Another start of thermi asks for the settings window, with the token the desktop gave it, or empty.</param>
internal sealed record OverlayAnswers(Func<long> HotkeyStatus, Func<OverlayState> State, Action<string> Heard, Action<string> Open);

/// <summary>
/// What the running overlay answers its own programs on the session's bus, under its name <see cref="ShellLink.Name"/>:
/// the settings window asks it which hotkeys the desktop refused, as Windows' asks the overlay's hidden window, and tells
/// it each message of their link; <c>thermi-cli</c> and the window's map ask its <see cref="OverlayState"/>; and a second
/// start of thermi asks it to open the window, as Windows' second start does. The overlay's actions keep
/// <see cref="ShellLink.ObjectPath"/>, which GDBus serves itself.
/// </summary>
internal sealed class OverlayObject(OverlayAnswers answers) : IBusObject
{
    public const string Path = ShellLink.ObjectPath + "/Overlay";

    public const string Name = "io.github.claudiopi.thermi.Overlay";

    public const string HotkeyStatus = "HotkeyStatus";

    public const string State = "State";

    public const string Message = "Message";

    public const string Open = "Open";

    private static readonly BusInterface Shape = new(
        Name, [(HotkeyStatus, "", "x"), (State, "", OverlayState.Arguments), (Message, "s", ""), (Open, "s", "")], [], []);

    public BusInterface Interface => Shape;

    public GLib.Variant Property(string name) => throw new ArgumentException($"{Name} has no property {name}.", nameof(name));

    public GLib.Variant? Call(string method, GLib.Variant args)
    {
        switch (method)
        {
            case State:
                return answers.State().Variant();
            case Message:
                answers.Heard(args.GetChildValue(0).GetString(out _));
                return null;
            case Open:
                answers.Open(args.GetChildValue(0).GetString(out _));
                return null;
            default:
                return GLib.Variant.NewTuple([GLib.Variant.NewInt64(answers.HotkeyStatus())]);
        }
    }
}
