using System.Globalization;

namespace Thermi.Sensors.Linux.Reading;

/// <summary>
/// The kernel's counters, as bytes moved, energy used or time busy, turned into what changed since the second before.
/// A counter's first reading, a missing one, one with no clock, or one that went back, as after a reset, gives no change:
/// a missing value, never a zero. A counter that wraps, as RAPL's energy does, gives the change across its wrap.
/// </summary>
internal sealed class Counters
{
    private readonly Dictionary<string, (double Value, double? At)> _last = new(StringComparer.Ordinal);

    /// <summary>A whole number of a file of the kernel's, or null for one that is missing or does not read as one.</summary>
    public static double? Number(string? text) =>
        long.TryParse(text.AsSpan().Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>How much <paramref name="key"/>'s counter rose since it was last given, or null when that cannot be told.</summary>
    public double? Change(string key, double? value, double wrap = 0) => Rise(key, value, at: null, wrap).Rise;

    /// <summary>
    /// How fast <paramref name="key"/>'s counter rose since it was last given, per second of <paramref name="at"/>, the
    /// clock that only goes forward, or null when that cannot be told.
    /// </summary>
    public double? Rate(string key, double? value, double? at, double wrap = 0)
    {
        var (rise, seconds) = Rise(key, value, at, wrap);
        return rise is { } risen && seconds is > 0 ? risen / seconds : null;
    }

    private (double? Rise, double? Seconds) Rise(string key, double? value, double? at, double wrap)
    {
        if (value is not { } now)
        {
            _last.Remove(key);
            return (null, null);
        }

        var had = _last.TryGetValue(key, out var last);
        _last[key] = (now, at);
        if (!had)
        {
            return (null, null);
        }

        var rise = now >= last.Value ? now - last.Value : wrap > 0 ? now + wrap - last.Value : (double?)null;
        return (rise, at - last.At);
    }
}
