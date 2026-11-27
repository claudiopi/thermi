namespace Thermi.Core.Updates;

/// <summary>
/// The channel a Linux PC follows, as the repository file its package added names it: apt's suite, or the end of dnf's
/// address. A person who wants beta changes it there (docs/RELEASES.md).
/// </summary>
public static class SystemRepository
{
    /// <summary>
    /// beta where the file names it, else stable: the packages add stable, and a PC with no repository of thermi's, as one
    /// on the AUR, has no other.
    /// </summary>
    public static string Channel(string? aptSources, string? dnfRepo)
    {
        var named = Field(aptSources, "Suites:") ?? Field(dnfRepo, "baseurl=");
        return named?.TrimEnd('/').EndsWith("beta", StringComparison.Ordinal) == true ? "beta" : "stable";
    }

    private static string? Field(string? text, string name) => text?.Split('\n').Select(line => line.Trim())
        .FirstOrDefault(line => line.StartsWith(name, StringComparison.Ordinal))?[name.Length..].Trim();
}
