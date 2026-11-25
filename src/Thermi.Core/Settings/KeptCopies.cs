using System.Globalization;

namespace Thermi.Core.Settings;

/// <summary>
/// The copies the settings store keeps beside the file, as <c>config.&lt;why&gt;-&lt;when&gt;.json</c>: a broken file set
/// aside, or the file of an earlier format, named <c>v&lt;N&gt;</c>, which a version rolled back to reads again.
/// </summary>
public static class KeptCopies
{
    /// <summary>A name for a copy of <paramref name="file"/> kept for <paramref name="why"/> at <paramref name="local"/>, one no file has yet.</summary>
    public static string Name(string file, string why, DateTimeOffset local, Func<string, bool> taken)
    {
        var stamp = local.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var stem = Path.Combine(Path.GetDirectoryName(file) ?? "", $"{Path.GetFileNameWithoutExtension(file)}.{why}-{stamp}");
        var extension = Path.GetExtension(file);
        var name = stem + extension;
        var repeat = 1;
        while (taken(name))
        {
            repeat++;
            name = string.Create(CultureInfo.InvariantCulture, $"{stem}-{repeat}{extension}");
        }

        return name;
    }

    /// <summary>Of the <paramref name="names"/> beside <paramref name="file"/>, the newest copy kept in <paramref name="version"/>'s format, or null.</summary>
    public static string? Newest(IEnumerable<string> names, string file, int version)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(file)}.v{version}-");
        var extension = Path.GetExtension(file);
        return names
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Max(StringComparer.Ordinal);
    }
}
