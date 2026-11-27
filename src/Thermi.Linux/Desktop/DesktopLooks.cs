using System.Globalization;
using Thermi.Core.Look;

namespace Thermi.Linux.Desktop;

/// <summary>What the overlay's look and items read from the desktop and the machine on Linux, the preview's alike.</summary>
internal static class DesktopLooks
{
    private const string BaseFrequency = "/sys/devices/system/cpu/cpu0/cpufreq/base_frequency";

    // GNOME's top bar is dark in both schemes, and the panels of KDE Plasma and wlroots' bars follow no setting a
    // program can read alike, so the overlay's pills are a dark panel's.
    public const bool LightPanels = false;

    /// <summary>
    /// The CPU's base clock in MHz, as Intel's driver tells it in kHz; null where nothing tells it, as on most AMD CPUs,
    /// and then no throttling is told apart from a light load.
    /// </summary>
    public static double? BaseClock()
    {
        try
        {
            return double.TryParse(File.ReadAllText(BaseFrequency).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var kHz) && kHz > 0
                ? kHz / 1000
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The portal says only that high contrast is asked, not in which colors: these are GNOME's high contrast's, white on
    // black with its blue.
    public static readonly HighContrastColors Contrasted = new(new Rgb(0, 0, 0), new Rgb(255, 255, 255), new Rgb(0x35, 0x84, 0xE4), new Rgb(0x99, 0xC1, 0xF1));

    /// <summary>
    /// The colors of high contrast when the desktop asks for it, as the settings portal says every desktop's choice; null
    /// otherwise, or where no portal answers, as in a session with none.
    /// </summary>
    public static HighContrastColors? HighContrast() => AsksHighContrast() ? Contrasted : null;

    private static bool AsksHighContrast()
    {
        try
        {
            // The session's one connection, which GirCore hands out as one object: disposing it would close it for the overlay too.
            var bus = Gio.Functions.BusGetSync(Gio.BusType.Session, null);
            using var call = GLib.Variant.NewTuple([GLib.Variant.NewString("org.freedesktop.appearance"), GLib.Variant.NewString("contrast")]);
            using var answer = bus.CallSync(
                "org.freedesktop.portal.Desktop",
                "/org/freedesktop/portal/desktop",
                "org.freedesktop.portal.Settings",
                "ReadOne",
                call,
                null,
                Gio.DBusCallFlags.None,
                1000,
                null);
            using var value = answer.GetChildValue(0).GetVariant();
            return value.GetUint32() == 1;
        }
        catch (GLib.GException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a newer settings file is a rollback this process should take back: never on Linux, whose packages are its
    /// only updater, so a newer file stays as it is.
    /// </summary>
    public static Func<bool> RunsInstalled { get; } = static () => false;
}
