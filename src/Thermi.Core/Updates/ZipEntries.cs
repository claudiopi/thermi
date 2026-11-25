namespace Thermi.Core.Updates;

/// <summary>Which entries of an update's zip may be unpacked: each must land inside the folder it is unpacked into.</summary>
public static class ZipEntries
{
    // A Unix link's type, in the top four bits of a zip entry's external attributes.
    private const uint LinkType = 0xA;

    private const uint TypeUnit = 1u << 28;

    /// <summary>
    /// Whether the entry <paramref name="name"/>, with <paramref name="externalAttributes"/>, lands inside
    /// <paramref name="folder"/>: never a rooted path, one naming a drive or a stream, one that climbs out, or a link.
    /// </summary>
    public static bool LandsInside(string folder, string name, int externalAttributes)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return !Path.IsPathRooted(name)
            && !name.Contains(':', StringComparison.Ordinal)
            && Path.GetFullPath(Path.Combine(folder, name)).StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && unchecked((uint)externalAttributes) / TypeUnit != LinkType;
    }
}
