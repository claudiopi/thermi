using System.Globalization;

namespace Thermi.Core;

/// <summary>
/// A whole number of a command line, in a range, as every program of thermi's reads one: the services' <c>--seconds</c> on
/// Windows and Linux, <c>thermi-cli</c>'s and <c>thermi</c>'s, which so take and refuse the same, and say it alike.
/// </summary>
public static class WholeNumber
{
    /// <summary>The number of the one token given, or null where it is not one from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static int? Parse(IReadOnlyList<string> tokens, int min, int max) =>
        tokens is [var token] && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max
            ? value
            : null;

    /// <summary>What a command line says of <paramref name="name"/> given something else.</summary>
    public static string Expected(string name, int min, int max) =>
        max == int.MaxValue
            ? string.Create(CultureInfo.InvariantCulture, $"{name} takes a whole number from {min} up.")
            : string.Create(CultureInfo.InvariantCulture, $"{name} takes a whole number from {min} to {max}.");
}
