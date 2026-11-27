using Thermi.Core.Messages;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Window;

/// <summary>
/// What the settings window answers the overlay on the session's bus, at its GTK application's id: each message of their
/// link (docs/adr/0009-overlay-and-window-messages.md), Core's payload in base64, with the desktop's token when the
/// overlay lets it come in front, as a click on the desktop does. A payload that does not read is dropped.
/// </summary>
internal sealed class WindowObject(Action<WindowMessage, string> received, Action dropped) : IBusObject
{
    public const string Path = "/io/github/claudiopi/thermi/Window";

    public const string Name = "io.github.claudiopi.thermi.Window";

    public const string Message = "Message";

    private static readonly BusInterface Shape = new(Name, [(Message, "ss", "")], [], []);

    public BusInterface Interface => Shape;

    public GLib.Variant Property(string name) => throw new ArgumentException($"{Name} has no property {name}.", nameof(name));

    public GLib.Variant? Call(string method, GLib.Variant args)
    {
        if (Read(args.GetChildValue(0).GetString(out _)) is { } message)
        {
            received(message, args.GetChildValue(1).GetString(out _));
        }
        else
        {
            dropped();
        }

        return null;
    }

    /// <summary>A message as the other end sent it, or null for a payload that is none.</summary>
    public static WindowMessage? Read(string payload)
    {
        try
        {
            return WindowMessages.Decode(Convert.FromBase64String(payload));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
