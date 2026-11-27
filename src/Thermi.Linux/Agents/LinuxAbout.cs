using System.Reflection;
using Thermi.Core.Agents;

namespace Thermi.Linux.Agents;

/// <summary>
/// What About says on Linux: the build's version, the channel of the repository the packages added, which a person
/// changes in its file, and that updates come from the system's own updater (docs/adr/0019-linux.md).
/// </summary>
internal static class LinuxAbout
{
    // The files the packages' post-install writes: apt's names the channel as its suite, dnf's at the end of its address.
    private const string AptSources = "/etc/apt/sources.list.d/thermi.sources";

    private const string DnfRepo = "/etc/yum.repos.d/thermi.repo";

    public static AboutFacts Read() => new(
        typeof(LinuxAbout).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
        Channel(),
        "system");

    // A repository that names beta follows beta; every other PC, with no repository of thermi's too, follows stable.
    private static string Channel()
    {
        var named = Field(AptSources, "Suites:") ?? Field(DnfRepo, "baseurl=");
        return named?.TrimEnd('/').EndsWith("beta", StringComparison.Ordinal) == true ? "beta" : "stable";
    }

    private static string? Field(string path, string name) => File.Exists(path)
        ? File.ReadLines(path).Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith(name, StringComparison.Ordinal))?[name.Length..].Trim()
        : null;
}
