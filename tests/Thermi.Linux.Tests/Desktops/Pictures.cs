using System.Globalization;
using System.Text.RegularExpressions;

namespace Thermi.Linux.Tests.Desktops;

/// <summary>
/// Screenshots against the pictures thermi paints, through ImageMagick in the test's session: where the screen shows a
/// picture, and where a picture draws nothing, for a click to pass through.
/// </summary>
internal static partial class Pictures
{
    /// <summary>
    /// The pixels of <paramref name="picture"/> that the screen shows otherwise at (<paramref name="x"/>, <paramref name="y"/>),
    /// each channel within 1 of its value, as painting rounds: 0 when the screen shows it there.
    /// </summary>
    public static async Task<long> DifferingAsync(OwnSession session, string screenshot, string picture, int x, int y, CancellationToken cancel)
    {
        var (width, height) = await SizeAsync(session, picture, cancel).ConfigureAwait(false);
        var crop = string.Create(CultureInfo.InvariantCulture, $"{screenshot}[{width}x{height}+{x}+{y}]");
        // compare writes the count on stderr, and exits 1 when the pictures differ at all.
        var compared = await session.RunAsync("compare", ["-metric", "AE", "-fuzz", "0.4%", crop, picture, "null:"], cancel: cancel).ConfigureAwait(false);
        return compared.Exit is 0 or 1
            ? long.Parse(Count().Match(compared.Error).Value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"compare failed: {compared.Error}");
    }

    /// <summary>The same region of two screenshots, the pixels that differ.</summary>
    public static async Task<long> ChangedAsync(
        OwnSession session, string before, string after, (int X, int Y, int Width, int Height) region, CancellationToken cancel)
    {
        var (x, y, width, height) = region;
        var crop = string.Create(CultureInfo.InvariantCulture, $"[{width}x{height}+{x}+{y}]");
        var compared = await session.RunAsync("compare", ["-metric", "AE", before + crop, after + crop, "null:"], cancel: cancel).ConfigureAwait(false);
        return long.Parse(Count().Match(compared.Error).Value, CultureInfo.InvariantCulture);
    }

    public static async Task<(int Width, int Height)> SizeAsync(OwnSession session, string picture, CancellationToken cancel)
    {
        var said = await session.RunAsync("identify", ["-format", "%w %h", picture], cancel: cancel).ConfigureAwait(false);
        var size = said.Out.Split(' ').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        return (size[0], size[1]);
    }

    /// <summary>A point of <paramref name="picture"/> it leaves fully transparent, from its top left.</summary>
    public static async Task<(int X, int Y)> NothingAtAsync(OwnSession session, string picture, CancellationToken cancel)
    {
        var pixels = await session.RunAsync("convert", [picture, "-alpha", "extract", "txt:-"], cancel: cancel).ConfigureAwait(false);
        var clear = Clear().Match(pixels.Out);
        return clear.Success
            ? (int.Parse(clear.Groups["x"].Value, CultureInfo.InvariantCulture), int.Parse(clear.Groups["y"].Value, CultureInfo.InvariantCulture))
            : throw new InvalidOperationException($"{picture} draws everywhere.");
    }

    [GeneratedRegex(@"^\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Count();

    [GeneratedRegex(@"^(?<x>\d+),(?<y>\d+): \(0\)", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Clear();
}
