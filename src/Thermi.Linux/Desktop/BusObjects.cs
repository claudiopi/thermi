using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Desktop;

/// <summary>A D-Bus interface thermi serves: each method's arguments in and out, each signal's, and each property's type.</summary>
internal sealed record BusInterface(
    string Name,
    IReadOnlyList<(string Name, string In, string Out)> Methods,
    IReadOnlyList<(string Name, string Args)> Signals,
    IReadOnlyList<(string Name, string Type)> Properties)
{
    /// <summary>The interface as D-Bus' introspection describes it, which tools as <c>gdbus call</c> read to type their arguments.</summary>
    public string Xml()
    {
        var xml = new StringBuilder($"<interface name=\"{Name}\">");
        foreach (var (name, into, back) in Methods)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<method name=\"{name}\">");
            Types(into).ToList().ForEach(type => xml.Append(CultureInfo.InvariantCulture, $"<arg type=\"{type}\" direction=\"in\"/>"));
            Types(back).ToList().ForEach(type => xml.Append(CultureInfo.InvariantCulture, $"<arg type=\"{type}\" direction=\"out\"/>"));
            xml.Append("</method>");
        }

        foreach (var (name, args) in Signals)
        {
            var types = string.Concat(Types(args).Select(type => $"<arg type=\"{type}\"/>"));
            xml.Append(CultureInfo.InvariantCulture, $"<signal name=\"{name}\">{types}</signal>");
        }

        foreach (var (name, type) in Properties)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<property name=\"{name}\" type=\"{type}\" access=\"read\"/>");
        }

        return xml.Append("</interface>").ToString();
    }

    // A signature's complete types, each a basic type, a variant, an array of one, a struct or a dictionary's entry.
    private static IEnumerable<string> Types(string signature)
    {
        for (var at = 0; at < signature.Length;)
        {
            var end = End(signature, at);
            yield return signature[at..end];
            at = end;
        }
    }

    private static int End(string signature, int at) => signature[at] switch
    {
        'a' => End(signature, at + 1),
        '(' or '{' => Closing(signature, at + 1),
        _ => at + 1,
    };

    private static int Closing(string signature, int at)
    {
        while (signature[at] is not (')' or '}'))
        {
            at = End(signature, at);
        }

        return at + 1;
    }
}

/// <summary>An object thermi serves on the session's bus, called only on the GLib main loop's thread.</summary>
internal interface IBusObject
{
    BusInterface Interface { get; }

    GLib.Variant Property(string name);

    /// <summary>
    /// The answer to a method of <see cref="Interface"/>, called with its arguments' signature: a tuple, or null for none. An
    /// argument the method cannot take throws <see cref="ArgumentException"/>, which the caller hears as invalid.
    /// </summary>
    GLib.Variant? Call(string method, GLib.Variant args);
}

/// <summary>
/// The objects thermi serves at names of its own on the session's bus, as a tray asks its icons: GirCore cannot hand GDBus a
/// table of callbacks, so each call to one of those names is taken off the bus as it comes, answered on the GLib main loop,
/// and the calls of D-Bus' own interfaces, a property's or the introspection's, answered for every object alike. A call of
/// another shape, or to no object, is refused with D-Bus' error, never thrown.
/// </summary>
internal sealed class BusObjects : IDisposable
{
    private const string Errors = "org.freedesktop.DBus.Error.";
    private const string Properties = "org.freedesktop.DBus.Properties";
    private const string Introspectable = "org.freedesktop.DBus.Introspectable";

    private const string Standard = $$"""
        <interface name="{{Introspectable}}"><method name="Introspect"><arg type="s" direction="out"/></method></interface>
        <interface name="{{Properties}}"><method name="Get"><arg type="s" direction="in"/><arg type="s" direction="in"/>
        <arg type="v" direction="out"/></method><method name="GetAll"><arg type="s" direction="in"/>
        <arg type="a{sv}" direction="out"/></method></interface>
        """;

    private readonly Gio.DBusConnection _bus;
    private readonly Action<Action> _post;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<(string Name, string Path), IBusObject> _objects = new();
    private readonly uint _filter;

    public BusObjects(Gio.DBusConnection bus, Action<Action> post, ILogger log)
    {
        (_bus, _post, _log) = (bus, post, log);
        _filter = bus.AddFilter(Filter);
    }

    public void Serve(string name, string path, IBusObject served) => _objects[(name, path)] = served;

    /// <summary>Serves nothing more at <paramref name="name"/>.</summary>
    public void Drop(string name) => _objects.Keys.Where(key => key.Name == name).ToList().ForEach(key => _objects.TryRemove(key, out _));

    /// <summary>Tells a signal of the object at <paramref name="path"/> to whoever listens.</summary>
    public void Emit(string path, string signal, IBusObject from, GLib.Variant? args = null) =>
        _bus.EmitSignal(null, path, from.Interface.Name, signal, args);

    public void Dispose() => _bus.RemoveFilter(_filter);

    // GDBus calls it on its own thread with every message in and out; it holds only what the main loop answers.
    private Gio.DBusMessage? Filter(Gio.DBusConnection connection, Gio.DBusMessage message, bool incoming)
    {
        if (!incoming || message.GetMessageType() != Gio.DBusMessageType.MethodCall || message.GetDestination() is not { } name
            || !_objects.Keys.Any(key => key.Name == name))
        {
            return message;
        }

        _post(() => Answer(message));
        return null;
    }

    private void Answer(Gio.DBusMessage call)
    {
        Gio.DBusMessage reply;
        try
        {
            reply = Reply(call);
        }
        catch (ArgumentException ex)
        {
            reply = call.NewMethodErrorLiteral(Errors + "InvalidArgs", ex.Message);
        }
#pragma warning disable CA1031 // A call that fails is answered as failed, and thermi goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.TrayCallFailed(call.GetPath() ?? "", ex);
            reply = call.NewMethodErrorLiteral(Errors + "Failed", "thermi could not answer it.");
        }

        if (!call.GetFlags().HasFlag(Gio.DBusMessageFlags.NoReplyExpected))
        {
            _bus.SendMessage(reply, Gio.DBusSendMessageFlags.None, out _);
        }
    }

    private Gio.DBusMessage Reply(Gio.DBusMessage call)
    {
        var path = call.GetPath() ?? "";
        if (!_objects.TryGetValue((call.GetDestination()!, path), out var served))
        {
            return call.NewMethodErrorLiteral(Errors + "UnknownObject", $"No object is at {path}.");
        }

        var args = call.GetBody() ?? GLib.Variant.NewTuple([]);
        var shape = args.GetTypeString();
        var (into, member) = (call.GetInterface() ?? served.Interface.Name, call.GetMember() ?? "");
        GLib.Variant? answer = (into, member, shape) switch
        {
            (Introspectable, "Introspect", "()") => Tuple(GLib.Variant.NewString($"<node>{Standard}{served.Interface.Xml()}</node>")),
            (Properties, "Get", "(ss)") when Of(args, 0) == served.Interface.Name => Tuple(GLib.Variant.NewVariant(Property(served, Of(args, 1)))),
            (Properties, "GetAll", "(s)") when Of(args, 0) == served.Interface.Name => Tuple(All(served)),
            (Properties, _, _) or (Introspectable, _, _) => throw new ArgumentException($"{into} has no {member}{shape} here.", nameof(call)),
            _ when into != served.Interface.Name => throw new ArgumentException($"No interface {into} is at {path}.", nameof(call)),
            _ => served.Interface.Methods.Any(method => method.Name == member && $"({method.In})" == shape)
                ? served.Call(member, args)
                : throw new ArgumentException($"{into} has no {member}{shape}.", nameof(call)),
        };
        var reply = call.NewMethodReply();
        if (answer is not null)
        {
            reply.SetBody(answer);
        }

        return reply;
    }

    private static string Of(GLib.Variant args, int at) => args.GetChildValue((nuint)at).GetString(out _);

    private static GLib.Variant Tuple(GLib.Variant value) => GLib.Variant.NewTuple([value]);

    private static GLib.Variant Property(IBusObject served, string name) => served.Interface.Properties.Any(property => property.Name == name)
        ? served.Property(name)
        : throw new ArgumentException($"{served.Interface.Name} has no property {name}.", nameof(name));

    private static GLib.Variant All(IBusObject served) => GLib.Variant.NewArray(
        GLib.VariantType.New("{sv}"),
        [.. served.Interface.Properties.Select(property =>
            GLib.Variant.NewDictEntry(GLib.Variant.NewString(property.Name), GLib.Variant.NewVariant(served.Property(property.Name))))]);
}
