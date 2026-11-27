using System.Globalization;

namespace Thermi.Core;

/// <summary>
/// A whole number of a command line, in a range, as every program of thermi's reads one: the services' <c>--seconds</c> on
/// Windows and Linux, <c>thermi-cli</c>'s and <c>thermi</c>'s, which so take and refuse the same, and say it alike.
/// </summary>
public static class WholeNumber
{
    /// <summary>The number of the token given, or null where there is none or it is not one from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static int? Parse(string? token, int min, int max) =>
        int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max ? value : null;

    /// <summary>What a command line says of <paramref name="name"/> given something else than a number in its range.</summary>
    public static string Expected(string name, int min, int max) =>
        string.Create(CultureInfo.InvariantCulture, $"{name} takes a whole number from {min} to {max}.");

    /// <summary>What a command line says of <paramref name="name"/> given something else than a number of at least <paramref name="min"/>.</summary>
    public static string Expected(string name, int min) => string.Create(CultureInfo.InvariantCulture, $"{name} takes a whole number from {min} up.");
}
