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

    /// <summary>
    /// Whether the desktop's panels are light, which "Match taskbar" follows as on Windows: KDE Plasma's as its Plasma theme
    /// paints them (<see cref="LightPlasmaPanels"/>). GNOME's top bar is dark in both schemes, and wlroots' bars and X11's
    /// panels follow no setting a program can read alike, so elsewhere the overlay's pills are a dark panel's.
    /// </summary>
    public static bool LightPanels()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string Dir(string variable, string fallback)
        {
            return Environment.GetEnvironmentVariable(variable) is { Length: > 0 } set ? set : Path.Combine(home, fallback);
        }

        static string[] Dirs(string variable, string fallback)
        {
            return (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } set ? set : fallback).Split(':', StringSplitOptions.RemoveEmptyEntries);
        }

        string[] data = [Dir("XDG_DATA_HOME", ".local/share"), .. Dirs("XDG_DATA_DIRS", "/usr/local/share:/usr/share")];
        return (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':').Contains("KDE", StringComparer.OrdinalIgnoreCase)
            && LightPlasmaPanels(Dir("XDG_CONFIG_HOME", ".config"), Dirs("XDG_CONFIG_DIRS", "/etc/xdg"), data);
    }

    /// <summary>
    /// Whether KDE Plasma's panels are painted in a light colour, one that contrasts more with black than with white, as its
    /// config files say it from <paramref name="configHome"/> and <paramref name="configDirs"/>, the defaults of the global
    /// theme the person chose among them, and its Plasma themes from <paramref name="dataDirs"/>: the window background of
    /// the Plasma theme's own colours, or of the colour scheme for a theme with none, as Breeze's. False where neither says it.
    /// </summary>
    public static bool LightPlasmaPanels(string configHome, IReadOnlyList<string> configDirs, IReadOnlyList<string> dataDirs) =>
        PlasmaPanel(configHome, configDirs, dataDirs) is { } panel && panel.ContrastWith(new Rgb(0, 0, 0)) > panel.ContrastWith(new Rgb(255, 255, 255));

    private static Rgb? PlasmaPanel(string configHome, IReadOnlyList<string> configDirs, IReadOnlyList<string> dataDirs)
    {
        // Plasma's session puts the chosen global theme's defaults after the person's own, as a program's environment may not.
        var defaults = Path.Combine(configHome, "kdedefaults");
        string[] configs = [configHome, .. configDirs.Contains(defaults) ? [] : new[] { defaults }, .. configDirs];
        string? Config(string file, string group, string key)
        {
            return configs.Select(dir => IniValue(Path.Combine(dir, file), group, key)).FirstOrDefault(value => value is not null);
        }

        var theme = Config("plasmarc", "Theme", "name") ?? "default";
        var colors = dataDirs.Select(dir => Path.Combine(dir, "plasma", "desktoptheme", theme)).FirstOrDefault(Directory.Exists) is { } found
            && File.Exists(Path.Combine(found, "colors"))
            ? IniValue(Path.Combine(found, "colors"), "Colors:Window", "BackgroundNormal")
            : Config("kdeglobals", "Colors:Window", "BackgroundNormal");
        var parts = (colors ?? "").Split(',')
            .Select(part => byte.TryParse(part, CultureInfo.InvariantCulture, out var channel) ? channel : (byte?)null)
            .ToList();
        return parts.Count >= 3 && parts[0] is { } red && parts[1] is { } green && parts[2] is { } blue ? new Rgb(red, green, blue) : null;
    }

    // A key of a group of a KDE config file, as KConfig writes it; null where the file, the group or the key is missing.
    private static string? IniValue(string file, string group, string key)
    {
        try
        {
            var lines = File.ReadAllLines(file);
            var start = Array.IndexOf(lines, $"[{group}]");
            return start < 0
                ? null
                : lines.Skip(start + 1).TakeWhile(line => !line.StartsWith('['))
                    .Where(line => line.StartsWith(key + "=", StringComparison.Ordinal))
                    .Select(line => line[(key.Length + 1)..].Trim())
                    .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
    /// The accent the overlay's marks draw in where the settings portal says none: high contrast's highlight, or GNOME's and
    /// KDE's default blue.
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
