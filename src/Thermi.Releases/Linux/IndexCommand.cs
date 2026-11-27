using System.CommandLine;

namespace Thermi.Releases.Linux;

/// <summary>
/// <c>thermi-releases index</c>: a channel's apt and dnf indexes for the packages it offers, unsigned, which the Linux
/// levers of <c>scripts/updates.ps1</c> sign and put in place (docs/adr/0019-linux.md).
/// </summary>
internal static class IndexCommand
{
    public static Command Create()
    {
        var repository = new Option<DirectoryInfo>("--repository") { Description = "The repositories' folder, with apt/ and rpm/.", Required = true };
        var channel = new Option<string>("--channel") { Description = "stable or beta.", Required = true };
        var debs = new Option<string[]>("--debs") { Description = "Each .deb the channel offers, under apt/.", AllowMultipleArgumentsPerToken = true };
        var rpms = new Option<string[]>("--rpms")
        {
            Description = "Each .rpm the channel offers, under rpm/<channel>/.",
            AllowMultipleArgumentsPerToken = true,
        };
        var output = new Option<DirectoryInfo>("--out") { Description = "Where the indexes go, laid out as in the repositories' folder.", Required = true };
        var command = new Command("index", "Writes a channel's apt and dnf indexes, unsigned.") { repository, channel, debs, rpms, output };
        command.SetAction(parsed =>
        {
            var root = parsed.GetValue(repository)!.FullName;
            var name = parsed.GetValue(channel)!;
            var into = parsed.GetValue(output)!.FullName;
            var now = TimeProvider.System.GetUtcNow();
            AptIndex.Write(Path.Combine(root, "apt"), parsed.GetValue(debs) ?? [], name, now, Path.Combine(into, "apt"));
            RpmIndex.Write(Path.Combine(root, "rpm", name), parsed.GetValue(rpms) ?? [], now, Path.Combine(into, "rpm", name));
            return 0;
        });
        return command;
    }
}
