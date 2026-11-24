using System.CommandLine;
using System.Globalization;
using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.Releases;

/// <summary>
/// <c>thermi-releases</c>: the owner's levers on the update channels, each writing the channel's next signed manifest into
/// a channels folder, and <c>serve</c>, which answers that folder on this PC. The private key comes from <c>--key</c>, a PEM
/// file, or the <c>THERMI_UPDATES_KEY</c> variable holding the PEM itself, as the release workflow gives it. The two public
/// keys, which open each channel's signed history, come from <c>--trust</c>, a folder with <c>release.pub.pem</c> and
/// <c>backup.pub.pem</c>, or the <c>THERMI_UPDATES_RELEASE_PUB</c> and <c>THERMI_UPDATES_BACKUP_PUB</c> variables.
/// </summary>
internal static class Program
{
    private static readonly Option<DirectoryInfo> Folder = new("--folder") { Description = "The channels folder.", Required = true };
    private static readonly Option<FileInfo?> Key = new("--key") { Description = "The private key's PEM; else THERMI_UPDATES_KEY." };
    private static readonly Option<string> Channel = new("--channel") { Description = "stable or beta.", DefaultValueFactory = _ => Manifest.Stable };
    private static readonly Option<int> Rollout = new("--rollout") { Description = "The percentage of PCs that take it.", DefaultValueFactory = _ => 100 };
    private static readonly Option<bool> Urgent = new("--urgent") { Description = "Switches without waiting for a quiet moment." };
    private static readonly Option<DirectoryInfo?> Trust = new("--trust")
    {
        Description = "The folder with release.pub.pem and backup.pub.pem; else THERMI_UPDATES_RELEASE_PUB and THERMI_UPDATES_BACKUP_PUB.",
    };

    private static int Main(string[] args) => Root().Parse(args).Invoke();

    private static RootCommand Root()
    {
        var root = new RootCommand("thermi-releases: the levers on thermi's update channels (docs/adr/0008-updates.md).");
        root.Subcommands.Add(Keys());
        root.Subcommands.Add(Publish());
        root.Subcommands.Add(Promote());
        root.Subcommands.Add(Lever("rollback", "Withdraws the channel's version and offers the one before it.", Levers.RollBack));
        root.Subcommands.Add(Lever("disable", "Keeps every PC on the channel where it is.", Levers.Disable));
        root.Subcommands.Add(Lever("refresh", "Signs the channel's manifest again before it expires.", Levers.Refresh));
        root.Subcommands.Add(Revoke());
        root.Subcommands.Add(Lever("unrevoke", "Clears the channel's revocation, once PCs run a build with the new release key.", Levers.Unrevoke));
        root.Subcommands.Add(Status());
        root.Subcommands.Add(Serve());
        return root;
    }

    private static Command Keys()
    {
        var output = new Option<DirectoryInfo>("--out") { Description = "Where the two key pairs go.", Required = true };
        var command = new Command("keys", "Makes the release and backup key pairs, each a private and a public PEM.") { output };
        command.SetAction(parsed =>
        {
            var folder = parsed.GetValue(output)!;
            folder.Create();
            foreach (var name in new[] { "release", "backup" })
            {
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                // Never over a key there: the backup pair is the one every PC trusts once a release key is revoked.
                New(Path.Combine(folder.FullName, name + ".key.pem"), key.ExportPkcs8PrivateKeyPem());
                New(Path.Combine(folder.FullName, name + ".pub.pem"), key.ExportSubjectPublicKeyInfoPem());
            }

            Console.WriteLine($"Wrote release and backup keys to {folder.FullName}. Keep the backup's private key offline.");
            return 0;
        });
        return command;
    }

    private static void New(string path, string text)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(file);
        writer.Write(text);
    }

    private static Command Publish()
    {
        var version = new Option<string>("--version") { Description = "The build's version.", Required = true };
        var zips = new Option<string[]>("--zip")
        {
            Description = "processor=path of each zip, x64 and arm64.",
            Required = true,
            AllowMultipleArgumentsPerToken = true,
        };
        var baseUrl = new Option<string>("--base-url") { Description = "Where the folder is served, ending in /v1/.", Required = true };
        var command = new Command("publish", "Puts a build on a channel.") { Folder, Trust, Key, Channel, version, zips, baseUrl, Rollout, Urgent };
        command.SetAction(parsed => Run(parsed, (channels, folder, now) =>
        {
            var release = ReleaseVersion.Parse(parsed.GetValue(version)) ?? throw new InvalidOperationException("--version is not a release version.");
            var packages = parsed.GetValue(zips)!.Select(zip => zip.Split('=', 2)).ToDictionary(
                pair => pair[0],
                pair => folder.Add(release, pair[0], pair[1], new Uri(parsed.GetValue(baseUrl)!)),
                StringComparer.Ordinal);
            return Levers.Publish(channels, parsed.GetValue(Channel)!, release, packages, parsed.GetValue(Rollout), parsed.GetValue(Urgent), now);
        }));
        return command;
    }

    private static Command Promote()
    {
        var from = new Option<string>("--from") { DefaultValueFactory = _ => Manifest.Beta };
        var to = new Option<string>("--to") { DefaultValueFactory = _ => Manifest.Stable };
        var command = new Command("promote", "Takes one channel's version, files and all, to another.") { Folder, Trust, Key, from, to, Rollout, Urgent };
        command.SetAction(parsed => Run(parsed, (channels, _, now) => Levers.Promote(
            channels, parsed.GetValue(from)!, parsed.GetValue(to)!, parsed.GetValue(Rollout), parsed.GetValue(Urgent) ? true : null, now)));
        return command;
    }

    // Signed with the backup key alone, never the release key's variable: every PC refuses a revocation the release key
    // signed, and the channel would go dark. The hash is the one PCs compare, of the public key itself, not of its file.
    private static Command Revoke()
    {
        var backup = new Option<FileInfo>("--backup-key") { Description = "The backup key's private PEM.", Required = true };
        var release = new Option<FileInfo>("--release-key") { Description = "The revoked release key's public PEM.", Required = true };
        var command = new Command("revoke", "Names the release key as revoked, signed with the backup key.") { Folder, Trust, Channel, backup, release };
        command.SetAction(parsed =>
        {
            using var signer = SigningKey(parsed.GetValue(backup));
            using var revoked = ECDsa.Create();
            revoked.ImportFromPem(File.ReadAllText(parsed.GetValue(release)!.FullName));
            var hash = Convert.ToHexStringLower(SHA256.HashData(revoked.ExportSubjectPublicKeyInfo()));
            if (Convert.ToHexStringLower(SHA256.HashData(signer.ExportSubjectPublicKeyInfo())) == hash)
            {
                Console.Error.WriteLine("--backup-key is the release key it revokes: sign with the backup key.");
                return 1;
            }

            return Run(parsed, signer, (channels, _, now) => Levers.Revoke(channels, parsed.GetValue(Channel)!, hash, now));
        });
        return command;
    }

    private static Command Lever(string name, string description, Func<IReadOnlyDictionary<string, ChannelHistory>, string, DateTimeOffset, LeverResult> lever)
    {
        var command = new Command(name, description) { Folder, Trust, Key, Channel };
        command.SetAction(parsed => Run(parsed, (channels, _, now) => lever(channels, parsed.GetValue(Channel)!, now)));
        return command;
    }

    private static Command Status()
    {
        var command = new Command("status", "Shows what each channel serves.") { Folder, Trust };
        command.SetAction(parsed =>
        {
            foreach (var (channel, history) in FolderOf(parsed).Read())
            {
                Console.WriteLine(history.Current is { } current ? Describe(channel, current) : $"{channel}: nothing published");
            }

            return 0;
        });
        return command;
    }

    private static Command Serve()
    {
        var port = new Option<int>("--port") { DefaultValueFactory = _ => 8765 };
        var command = new Command("serve", "Answers the channels folder on this PC, for a test build.") { Folder, port };
        command.SetAction((parsed, stop) => Server.RunAsync(parsed.GetValue(Folder)!.FullName, parsed.GetValue(port), Console.Out, stop));
        return command;
    }

    private static int Run(ParseResult parsed, Func<IReadOnlyDictionary<string, ChannelHistory>, ChannelFolder, DateTimeOffset, LeverResult> lever)
    {
        using var key = SigningKey(parsed.GetValue(Key));
        return Run(parsed, key, lever);
    }

    private static int Run(
        ParseResult parsed, ECDsa key, Func<IReadOnlyDictionary<string, ChannelHistory>, ChannelFolder, DateTimeOffset, LeverResult> lever)
    {
        var folder = FolderOf(parsed);
        var before = folder.Read();
        var result = lever(before, folder, TimeProvider.System.GetUtcNow());
        if ((result.Refused ?? folder.Refuses(before, result.Channels, key)) is { } refused)
        {
            Console.Error.WriteLine(refused);
            return 1;
        }

        foreach (var manifest in folder.Write(before, result.Channels, key))
        {
            Console.WriteLine(Describe(manifest.Channel, manifest));
        }

        return 0;
    }

    private static ChannelFolder FolderOf(ParseResult parsed)
    {
        var trust = parsed.GetValue(Trust);
        return new ChannelFolder(parsed.GetValue(Folder)!.FullName, PublicKey(trust, "release"), PublicKey(trust, "backup"));
    }

    private static byte[] PublicKey(DirectoryInfo? trust, string name)
    {
        var variable = $"THERMI_UPDATES_{name.ToUpperInvariant()}_PUB";
        var pem = trust is not null ? File.ReadAllText(Path.Combine(trust.FullName, name + ".pub.pem")) : Environment.GetEnvironmentVariable(variable)
            ?? throw new InvalidOperationException($"Give --trust or {variable}.");
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key.ExportSubjectPublicKeyInfo();
    }

    private static ECDsa SigningKey(FileInfo? file)
    {
        var pem = file is not null ? File.ReadAllText(file.FullName) : Environment.GetEnvironmentVariable("THERMI_UPDATES_KEY")
            ?? throw new InvalidOperationException("Give --key or THERMI_UPDATES_KEY.");
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    private static string Describe(string channel, Manifest manifest) => string.Create(
        CultureInfo.InvariantCulture,
        $"{channel}: {manifest.Version?.ToString() ?? "nothing"} to {manifest.Rollout} %"
            + $"{(manifest.Urgent ? ", urgent" : "")}{(manifest.Disabled ? ", turned off" : "")}, "
            + $"sequence {manifest.Sequence}, expires {manifest.Expires:yyyy-MM-dd}, withdrawn [{string.Join(", ", manifest.Withdrawn)}]");
}
