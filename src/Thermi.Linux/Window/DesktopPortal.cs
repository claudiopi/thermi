using System.Globalization;
using Microsoft.Extensions.Logging;
using Thermi.Linux.Desktop;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Window;

/// <summary>
/// The desktop's look, as the settings portal (<c>org.freedesktop.portal.Settings</c>) says every desktop's choice: its
/// light or dark scheme, its accent and whether it asks for high contrast, kept as it changes. A session whose portal
/// does not answer, or does not say, or says it in another shape, has no preference, no accent and no high contrast.
/// The bus is the session's one, which it never closes.
/// </summary>
internal sealed class DesktopPortal : IDisposable
{
    private readonly ILogger _log;
    private readonly Action _changed;
    private readonly Gio.DBusProxy? _signals;

    /// <param name="bus">The session's bus, or null where there is none, which says nothing.</param>
    /// <param name="changed">Runs on GTK's loop each time the desktop changes its scheme, accent or contrast.</param>
    /// <param name="log">Where a change it could not read goes.</param>
    public DesktopPortal(Gio.DBusConnection? bus, Action changed, ILogger log)
    {
        _log = log;
        _changed = changed;
        if (bus is null)
        {
            return;
        }

        using (var scheme = DesktopLooks.Appearance(bus, "color-scheme"))
        using (var accent = DesktopLooks.Appearance(bus, "accent-color"))
        using (var contrast = DesktopLooks.Appearance(bus, "contrast"))
        {
            (Scheme, Accent, HighContrast) = (SchemeOf(scheme), AccentOf(accent), DesktopLooks.ContrastOf(contrast));
        }

        // A proxy's signal comes through GObject's own, which GirCore calls back on the loop; a bare subscription's callback
        // it does not marshal, and the process dies at the first change.
        const Gio.DBusProxyFlags Flags = Gio.DBusProxyFlags.DoNotLoadProperties | Gio.DBusProxyFlags.DoNotAutoStart;
        _signals = Gio.DBusProxy.NewSync(bus, Flags, null, DesktopLooks.Portal, DesktopLooks.PortalPath, DesktopLooks.PortalFace, null);
        _signals.OnGSignal += (_, heard) => Heard(heard.SignalName, heard.Parameters);
    }

    /// <summary>True for dark, false for light, null where the desktop prefers neither.</summary>
    public bool? Scheme { get; private set; }

    /// <summary>The accent as <c>#rrggbb</c>, null where the desktop has none.</summary>
    public string? Accent { get; private set; }

    public bool HighContrast { get; private set; }

    public void Dispose()
    {
        _signals?.Dispose();
    }

    // 1 is dark and 2 light, 0 no preference.
    private static bool? SchemeOf(GLib.Variant? value) => value?.GetTypeString() != "u" ? null : value.GetUint32() switch
    {
        1 => true,
        2 => false,
        _ => null,
    };

    // Red, green and blue from 0 to 1; any outside says the desktop has no accent.
    private static string? AccentOf(GLib.Variant? value)
    {
        if (value?.GetTypeString() != "(ddd)")
        {
            return null;
        }

        var parts = Enumerable.Range(0, 3).Select(at => value.GetChildValue((nuint)at).GetDouble()).ToArray();
        return parts.All(part => part is >= 0 and <= 1)
            ? "#" + string.Concat(parts.Select(part => ((int)Math.Round(part * 255)).ToString("x2", CultureInfo.InvariantCulture)))
            : null;
    }

    private void Heard(string signal, GLib.Variant parameters)
    {
        try
        {
            if (signal == "SettingChanged" && parameters.GetTypeString() == "(ssv)"
                && parameters.GetChildValue(0).GetString(out _) == DesktopLooks.Appearances)
            {
                Changed(parameters.GetChildValue(1).GetString(out _), parameters.GetChildValue(2).GetVariant());
                _changed();
            }
        }
#pragma warning disable CA1031 // GLib calls it from native code, where an exception that escapes ends the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.PortalSignalFailed(signal, ex);
        }
    }

    private void Changed(string key, GLib.Variant value)
    {
        switch (key)
        {
            case "color-scheme":
                Scheme = SchemeOf(value);
                break;
            case "accent-color":
                Accent = AccentOf(value);
                break;
            case "contrast":
                HighContrast = DesktopLooks.ContrastOf(value);
                break;
            default:
                break;
        }
    }
}
