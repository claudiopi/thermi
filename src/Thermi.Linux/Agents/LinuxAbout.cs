using Thermi.Core.Agents;
using Thermi.Core.Install;
using Thermi.Core.Updates;

namespace Thermi.Linux.Agents;

/// <summary>
/// What About says on Linux: the build's version, the channel of the repository the packages added, and that updates
/// come from the system's own updater (docs/adr/0019-linux.md).
/// </summary>
internal static class LinuxAbout
{
    // The files the packages' post-install writes.
    private const string AptSources = "/etc/apt/sources.list.d/thermi.sources";

    private const string DnfRepo = "/etc/yum.repos.d/thermi.repo";

    public static AboutFacts Read() => new(BuildVersion.Of(typeof(LinuxAbout).Assembly), SystemRepository.Channel(Text(AptSources), Text(DnfRepo)), "system");

    private static string? Text(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
