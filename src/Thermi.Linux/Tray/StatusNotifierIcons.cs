using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.App.Gestures;
using Thermi.Linux.Desktop;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Tray;

/// <summary>
/// thermi's icons in the tray of every desktop but GNOME, as StatusNotifierItems registered with the session's
/// <c>org.kde.StatusNotifierWatcher</c>. Each icon has a bus name of its own, which thermi lets go as the icon goes: a
/// watcher forgets an item only as its name goes, and thermi's process holds every icon. A watcher that comes later, or
/// again, as a tray starts after thermi, has every icon registered again. A tray tells a left click with no time, so it is
/// timed as it comes, and two on one icon within the person's double-click time are a double-click.
/// </summary>
internal sealed class StatusNotifierIcons : ITrayIcons
{
    private const string Watcher = "org.kde.StatusNotifierWatcher";
    private const string Bus = "org.freedesktop.DBus";

    private readonly Gio.DBusConnection _bus;
    private readonly BusObjects _objects;
    private readonly ILogger _log;
    private readonly Dictionary<string, (string BusName, StatusNotifierItem Item)> _items = new(StringComparer.Ordinal);
    private readonly Gio.DBusProxy _daemon;
    private readonly TrayClicks _clicks;
    private readonly TimeProvider _time;
    private int _named;

    public StatusNotifierIcons(Gio.DBusConnection bus, BusObjects objects, ILogger log, PointerSettings pointer, TimeProvider time)
    {
        (_bus, _objects, _log, _time) = (bus, objects, log, time);
        _clicks = new TrayClicks(pointer);
        // A proxy hears the bus' signals, as GirCore cannot call back a subscription of the connection's own.
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotAutoStart;
        _daemon = Gio.DBusProxy.NewSync(bus, Flags, null, Bus, "/org/freedesktop/DBus", Bus, null);
        _daemon.OnGSignal += (_, heard) =>
            Callbacks.Safely(() => WatcherChanged(heard.SignalName, heard.Parameters), ex => _log.SignalFailed(heard.SignalName, ex));
    }

    public Action<string, TrayPress> Pressed { get; set; } = (_, _) => { };

    public Func<string, IReadOnlyList<ShellRow>> MenuOf { get; set; } = _ => [];

    public Action<string, int> Chosen { get; set; } = (_, _) => { };

    public Task Show(IReadOnlyList<TrayPicture> icons)
    {
        foreach (var gone in _items.Keys.Except(icons.Select(icon => icon.Key), StringComparer.Ordinal).ToList())
        {
            var released = _items[gone].BusName;
            _objects.Drop(released);
            _ = Safely(() => BusCall("ReleaseName", GLib.Variant.NewString(released)));
            _items.Remove(gone);
        }

        List<Task> registered = [];
        foreach (var icon in icons)
        {
            if (_items.TryGetValue(icon.Key, out var shown))
            {
                var menuChanged = shown.Item.Shows.ClickIsMenu != icon.ClickIsMenu;
                shown.Item.Shows = icon;
                // A tray reads every property again on any of the item's signals, ItemIsMenu with them.
                if (menuChanged)
                {
                    _objects.Emit(StatusNotifierItem.Path, StatusNotifierItem.NewStatus, shown.Item, GLib.Variant.NewTuple([GLib.Variant.NewString("Active")]));
                }

                Announce(icon.Redrawn, StatusNotifierItem.NewIcon, shown.Item);
                Announce(icon.Retipped, StatusNotifierItem.NewToolTip, shown.Item);
                continue;
            }

            var name = string.Create(CultureInfo.InvariantCulture, $"org.kde.StatusNotifierItem-{Environment.ProcessId}-{++_named}");
            var item = new StatusNotifierItem(icon, () => Pressed(icon.Key, _clicks.Click(icon.Key, Now())), press => Pressed(icon.Key, press));
            _objects.Serve(name, StatusNotifierItem.Path, item);
            _objects.Serve(name, TrayMenu.Path, new TrayMenu(() => MenuOf(icon.Key), row => Chosen(icon.Key, row)));
            _items[icon.Key] = (name, item);
            registered.Add(Safely(() => OwnAndRegisterAsync(name)));
        }

        return Task.WhenAll(registered);
    }

    public void OpenMenu(string key)
    {
        // A tray opens an icon's menu itself, from the item's Menu: on a right click, and on a left click where ItemIsMenu says
        // so. dbusmenu's ItemActivationRequested would open every icon's, as they share one path of one connection.
    }

    // Milliseconds, as a click's time is told elsewhere.
    private uint Now() => (uint)(_time.GetTimestamp() * 1000 / _time.TimestampFrequency);

    public void Dispose()
    {
        _daemon.Dispose();
        _items.Values.ToList().ForEach(item => _objects.Drop(item.BusName));
    }

    private async Task OwnAndRegisterAsync(string name)
    {
        // The name is thermi's alone, of its process and its count: one taken already is no one else's to queue for.
        const uint DoNotQueue = 4;
        await BusCall("RequestName", GLib.Variant.NewString(name), GLib.Variant.NewUint32(DoNotQueue));
        await RegisterAsync(name);
    }

    private async Task RegisterAsync(string name)
    {
        try
        {
            var item = GLib.Variant.NewTuple([GLib.Variant.NewString(name)]);
            await _bus.CallAsync(Watcher, "/StatusNotifierWatcher", Watcher, "RegisterStatusNotifierItem", item);
        }
        catch (GLib.GException ex)
        {
            _log.NoTray(ex);
        }
    }

    // Every icon is at the same path of thermi's one connection, so a tray reads each of them again on any one's signal.
    private void Announce(bool changed, string signal, StatusNotifierItem item)
    {
        if (changed)
        {
            _objects.Emit(StatusNotifierItem.Path, signal, item);
        }
    }

    private void WatcherChanged(string signal, GLib.Variant args)
    {
        if (signal == "NameOwnerChanged" && args.GetTypeString() == "(sss)" && args.GetChildValue(0).GetString(out _) == Watcher
            && args.GetChildValue(2).GetString(out _).Length > 0)
        {
            _items.Values.ToList().ForEach(item => _ = Safely(() => RegisterAsync(item.BusName)));
        }
    }

    private Task<GLib.Variant> BusCall(string method, params GLib.Variant[] args) =>
        _bus.CallAsync(Bus, "/org/freedesktop/DBus", Bus, method, GLib.Variant.NewTuple(args));

    // An icon the bus did not take is logged, and the others show all the same.
    private async Task Safely(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (GLib.GException ex)
        {
            _log.TrayCallFailed(StatusNotifierItem.Path, ex);
        }
    }
}
