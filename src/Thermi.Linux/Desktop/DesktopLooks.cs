using System.Globalization;
using Thermi.Core.Look;

namespace Thermi.Linux.Desktop;

/// <summary>What the overlay's look and items read from the desktop and the machine on Linux, the preview's alike.</summary>
internal static class DesktopLooks
{
    private const string BaseFrequency = "/sys/devices/system/cpu/cpu0/cpufreq/base_frequency";

    /// <summary>The settings portal every desktop answers, and its namespace of the desktop's look.</summary>
    public const string Portal = "org.freedesktop.portal.Desktop";

    public const string PortalPath = "/org/freedesktop/portal/desktop";

    public const string PortalFace = "org.freedesktop.portal.Settings";

    public const string Appearances = "org.freedesktop.appearance";

    private const int PortalTimeout = 1000;

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

    // The session's one connection, which GirCore hands out as one object: disposing it would close it for the overlay too.
    private static bool AsksHighContrast()
    {
        try
        {
            using var contrast = Appearance(Gio.Functions.BusGetSync(Gio.BusType.Session, null), "contrast");
            return ContrastOf(contrast);
        }
        catch (GLib.GException)
        {
            return false;
        }
    }

    /// <summary>
    /// The accent a drop's marks draw in: high contrast's highlight, or GNOME's and KDE's default blue, as no desktop's
    /// own accent reaches every one of them alike.
    /// </summary>
    public static Rgb Accent(HighContrastColors? highContrast) => highContrast?.Highlight ?? new Rgb(0x35, 0x84, 0xe4);

    /// <summary>
    /// The desktop's choice of <paramref name="key"/> in its appearance, as the settings portal answers it; null where the
    /// portal is not there, does not know the key or answers in another shape.
    /// </summary>
    public static GLib.Variant? Appearance(Gio.DBusConnection bus, string key)
    {
        try
        {
            using var call = GLib.Variant.NewTuple([GLib.Variant.NewString(Appearances), GLib.Variant.NewString(key)]);
            using var answer = bus.CallSync(Portal, PortalPath, PortalFace, "ReadOne", call, null, Gio.DBusCallFlags.None, PortalTimeout, null);
            if (answer.GetTypeString() != "(v)")
            {
                return null;
            }

            using var boxed = answer.GetChildValue(0);
            return boxed.GetVariant();
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    public static bool ContrastOf(GLib.Variant? value) => value?.GetTypeString() == "u" && value.GetUint32() == 1;

    /// <summary>
    /// Whether a newer settings file is a rollback this process should take back: never on Linux, whose packages are its
    /// only updater, so a newer file stays as it is.
    /// </summary>
    public static Func<bool> RunsInstalled { get; } = static () => false;
}
