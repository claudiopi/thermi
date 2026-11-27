using Thermi.Core.Agents;
using Thermi.Core.Install;
using Thermi.Core.Updates;

namespace Thermi.Linux.Agents;

/// <summary>
/// What About and status say on Linux: the build's version, the channel of the repository the packages added, and that
/// updates come from the system's own updater (docs/adr/0019-linux.md); a build run from anywhere else updates from nothing.
/// </summary>
internal static class LinuxAbout
{
    // The files the packages' post-install writes.
    private const string AptSources = "/etc/apt/sources.list.d/thermi.sources";

    private const string DnfRepo = "/etc/yum.repos.d/thermi.repo";

    // Where the packages install thermi.
    private const string InstallFolder = "/usr/lib/thermi/";

    public static bool Installed => AppContext.BaseDirectory.StartsWith(InstallFolder, StringComparison.Ordinal);

    public static UpdateLine Updates() => SystemRepository.Line(Installed ? Read().Channel : null);

    public static AboutFacts Read() => new(BuildVersion.Of(typeof(LinuxAbout).Assembly), SystemRepository.Channel(Text(AptSources), Text(DnfRepo)), "system");

    private static string? Text(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
}
