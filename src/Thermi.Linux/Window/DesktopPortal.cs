using System.Globalization;

namespace Thermi.Linux.Window;

/// <summary>
/// The desktop's look, as the settings portal (<c>org.freedesktop.portal.Settings</c>) says every desktop's choice: its
/// light or dark scheme, its accent and whether it asks for high contrast, kept as it changes. A session whose portal
/// does not answer, or does not say, has no preference, no accent and no high contrast.
/// </summary>
internal sealed class DesktopPortal : IDisposable
{
    private const string Portal = "org.freedesktop.portal.Desktop";
    private const string Path = "/org/freedesktop/portal/desktop";
    private const string Face = "org.freedesktop.portal.Settings";
    private const string Appearance = "org.freedesktop.appearance";
    private const int TimeoutMilliseconds = 1000;

    private readonly Gio.DBusConnection? _bus;
    private readonly uint _watch;

    /// <param name="changed">Runs on GTK's loop each time the desktop changes its scheme, accent or contrast.</param>
    public DesktopPortal(Action changed)
    {
        _bus = Connect();
        if (_bus is null)
        {
            return;
        }

        Scheme = Read("color-scheme") is { } scheme ? SchemeOf(scheme) : null;
        Accent = Read("accent-color") is { } accent ? AccentOf(accent) : null;
        HighContrast = Read("contrast") is { } contrast && contrast.GetUint32() == 1;
        _watch = _bus.SignalSubscribe(Portal, Face, "SettingChanged", Path, Appearance, Gio.DBusSignalFlags.None, (_, _, _, _, _, parameters) =>
        {
            Changed(parameters);
            changed();
        });
    }

    /// <summary>True for dark, false for light, null where the desktop prefers neither.</summary>
    public bool? Scheme { get; private set; }

    /// <summary>The accent as <c>#rrggbb</c>, null where the desktop has none.</summary>
    public string? Accent { get; private set; }

    public bool HighContrast { get; private set; }

    public void Dispose()
    {
        if (_bus is not null && _watch != 0)
        {
            _bus.SignalUnsubscribe(_watch);
        }

        _bus?.Dispose();
    }

    // 1 is dark and 2 light, 0 no preference.
    private static bool? SchemeOf(GLib.Variant value) => value.GetUint32() switch
    {
        1 => true,
        2 => false,
        _ => null,
    };

    // Red, green and blue from 0 to 1; any outside says the desktop has no accent.
    private static string? AccentOf(GLib.Variant value)
    {
        var parts = Enumerable.Range(0, 3).Select(at => value.GetChildValue((nuint)at).GetDouble()).ToArray();
        return parts.All(part => part is >= 0 and <= 1)
            ? "#" + string.Concat(parts.Select(part => ((int)Math.Round(part * 255)).ToString("x2", CultureInfo.InvariantCulture)))
            : null;
    }

    private static Gio.DBusConnection? Connect()
    {
        try
        {
            return Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    private void Changed(GLib.Variant parameters)
    {
        var key = parameters.GetChildValue(1).GetString(out _);
        var value = parameters.GetChildValue(2).GetVariant();
        switch (key)
        {
            case "color-scheme":
                Scheme = SchemeOf(value);
                break;
            case "accent-color":
                Accent = AccentOf(value);
                break;
            case "contrast":
                HighContrast = value.GetUint32() == 1;
                break;
            default:
                break;
        }
    }

    // A key the portal does not know, or a portal that is not there, says nothing.
    private GLib.Variant? Read(string key)
    {
        try
        {
            var answer = _bus!.CallSync(
                Portal,
                Path,
                Face,
                "ReadOne",
                GLib.Variant.NewTuple([GLib.Variant.NewString(Appearance), GLib.Variant.NewString(key)]),
                null,
                Gio.DBusCallFlags.None,
                TimeoutMilliseconds,
                null);
            return answer.GetChildValue(0).GetVariant();
        }
        catch (GLib.GException)
        {
            return null;
        }
    }
}
