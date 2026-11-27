using Thermi.Core.Install;

namespace Thermi.Core.Updates;

/// <summary>Which entries of an update's zip may be unpacked: each must land inside the folder it is unpacked into.</summary>
public static class ZipEntries
{
    // A Unix link's type, in the top four bits of a zip entry's external attributes.
    private const uint LinkType = 0xA;

    private const uint TypeUnit = 1u << 28;

    /// <summary>
    /// The full path the entry <paramref name="name"/>, with <paramref name="externalAttributes"/>, lands at inside
    /// <paramref name="folder"/>; null for a rooted path, one naming a drive or a stream, one that climbs out, or a link.
    /// </summary>
    public static string? Target(string folder, string name, int externalAttributes) =>
        Path.IsPathRooted(name) || name.Contains(':', StringComparison.Ordinal) || unchecked((uint)externalAttributes) / TypeUnit == LinkType
            ? null
            : InstallLayout.Within(Path.Combine(folder, name), folder);
}
