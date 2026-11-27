using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;

namespace Thermi.Linux.Overlay;

/// <summary>
/// What the running overlay answers its own programs on the session's bus, under its name <see cref="ShellLink.Name"/>:
/// the settings window asks it which hotkeys the desktop refused, as Windows' asks the overlay's hidden window, and
/// <c>thermi-cli</c> its <see cref="OverlayState"/>. The overlay's actions keep <see cref="ShellLink.ObjectPath"/>, which
/// GDBus serves itself.
/// </summary>
internal sealed class OverlayObject(Func<long> hotkeyStatus, Func<OverlayState> state) : IBusObject
{
    public const string Path = ShellLink.ObjectPath + "/Overlay";

    public const string Name = "io.github.claudiopi.thermi.Overlay";

    public const string HotkeyStatus = "HotkeyStatus";

    public const string State = "State";

    private static readonly BusInterface Shape = new(Name, [(HotkeyStatus, "", "x"), (State, "", OverlayState.Arguments)], [], []);

    public BusInterface Interface => Shape;

    public GLib.Variant Property(string name) => throw new ArgumentException($"{Name} has no property {name}.", nameof(name));

    public GLib.Variant? Call(string method, GLib.Variant args) => method == State
        ? state().Variant()
        : GLib.Variant.NewTuple([GLib.Variant.NewInt64(hotkeyStatus())]);
}
