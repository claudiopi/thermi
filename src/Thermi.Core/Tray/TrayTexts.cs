using System.Globalization;
using System.Text;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.Core.Tray;

/// <summary>Whether the sensor service's readings can be shown: they are fresh, the service is not running, or its data went stale.</summary>
[Translated]
public enum ServiceState
{
    Running,
    Stopped,
    Stale,
}

/// <summary>The parts of thermi's own tooltip, joined by " · ".</summary>
[Translated]
public enum TrayText
{
    /// <summary>A value at warning or critical, then the level's word.</summary>
    AtLevel,
    Warning,
    Critical,

    /// <summary>An overlay's name, hidden on its own.</summary>
    OverlayHidden,

    /// <summary>All overlays, hidden by the flag.</summary>
    OverlaysHidden,
    ClickThroughOn,

    /// <summary>In place of the value while the service is stopped or its data is stale.</summary>
    SensorsStopped,
}

/// <summary>What the tray icons say: an item's value in four characters, and the tooltips, cut to Windows' limit.</summary>
public static class TrayTexts
{
    /// <summary>The characters Windows shows of a tray icon's tooltip.</summary>
    public const int MaxTooltip = 127;

    /// <summary>The characters an item's icon has room for.</summary>
    public const int MaxShort = 4;

    public const string Ellipsis = "…";

    /// <summary>
    /// A value as the overlay rounds it, with its item's <paramref name="decimals"/> when it has them, without unit or
    /// thousands separators, with the regional decimal sign, in up to four characters: decimals are dropped when they do
    /// not fit, and a number still too long shows in thousands, as 12k. A throughput keeps its scale's letter, as 12M, and
    /// a time shows as 2:14, or in hours, as 12h, when that does not fit.
    /// </summary>
    public static string IconText(Reading reading, IFormatProvider culture, TemperatureUnit temperature = TemperatureUnit.Celsius, int? decimals = null)
    {
        if (reading.Usable is not { } value)
        {
            return ReadingFormatter.Missing;
        }

        if (reading.Kind is SensorKind.Duration or SensorKind.Time)
        {
            var clock = ReadingFormatter.Clock(value, culture);
            return clock.Length <= MaxShort ? clock : string.Concat(Math.Floor(value / 3600).ToString("0", culture), "h");
        }

        // An address's last number is the one that tells the PCs of a home network apart.
        if (reading.Kind == SensorKind.Address)
        {
            return string.Concat(".", ((uint)value & 255).ToString(CultureInfo.InvariantCulture));
        }

        var (number, own, _, letter) = ReadingFormatter.Parts(reading.Kind, value, temperature, Texts.English, reading.Bits);
        var digits = decimals ?? own;
        var text = number.ToString(ReadingFormatter.Digits(digits), culture) + letter;
        while (text.Length > MaxShort && digits > 0)
        {
            digits--;
            text = number.ToString(ReadingFormatter.Digits(digits), culture) + letter;
        }

        return text.Length > MaxShort ? string.Concat((number / 1000).ToString("0", culture), "k") : text;
    }

    /// <summary>
    /// An item icon's tooltip: its label, then its value with its unit, which it keeps when the overlay hides units. Without
    /// fresh readings it gives the label and says why, as the window does.
    /// </summary>
    public static string ItemTooltip(
        string? label,
        Reading? reading,
        ServiceState service,
        Texts texts,
        IFormatProvider culture,
        TemperatureUnit temperature,
        int? decimals = null)
    {
        string[] parts = service == ServiceState.Running
            ? [string.Join(
                ' ',
                new[]
                {
                    label,
                    reading is { } read ? ReadingFormatter.Format(read, culture, units: true, temperature, decimals, texts) : ReadingFormatter.Missing,
                }.OfType<string>())]
            : [.. new[] { label, texts.Of(service) }.OfType<string>()];
        return Fit(parts);
    }

    /// <summary>
    /// Lines joined into one text of at most <see cref="MaxTooltip"/> characters. A line too long alone is cut within it,
    /// and too many lines are cut at a whole line, an ellipsis counted in either way.
    /// </summary>
    public static string Fit(IReadOnlyList<string> lines)
    {
        var cut = lines.Select(line => Cut(line, MaxTooltip)).ToList();
        var whole = string.Join('\n', cut);
        if (whole.Length <= MaxTooltip)
        {
            return whole;
        }

        var kept = new StringBuilder();
        foreach (var line in cut)
        {
            var separator = kept.Length == 0 ? "" : "\n";
            if (kept.Length + separator.Length + line.Length + 1 + Ellipsis.Length > MaxTooltip)
            {
                break;
            }

            kept.Append(separator).Append(line);
        }

        var shown = kept.ToString().TrimEnd('\n');
        return shown.Length == 0 ? cut[0][..Math.Min(cut[0].Length, MaxTooltip - Ellipsis.Length)] + Ellipsis : string.Concat(shown, "\n", Ellipsis);
    }

    private static string Cut(string line, int max) => line.Length <= max ? line : string.Concat(line.AsSpan(0, max - Ellipsis.Length), Ellipsis);
}
