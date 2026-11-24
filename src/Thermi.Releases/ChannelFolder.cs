using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.Releases;

/// <summary>
/// A folder laid out as the update server is (docs/adr/0008-updates.md): each channel's signed envelope at the top,
/// the zips under <c>zips/&lt;version&gt;/</c>, which never change once written, and each channel's history of manifests
/// under <c>history/</c>, which only the levers read, signed by the key that sealed its last manifest.
/// </summary>
internal sealed class ChannelFolder(string root, byte[] release, byte[] backup)
{
    public string Root => root;

    /// <summary>Each channel's history; throws for one neither key signed, which no lever wrote, so none signs over it.</summary>
    public IReadOnlyDictionary<string, ChannelHistory> Read() => Manifest.Channels.ToDictionary(channel => channel, Read, StringComparer.Ordinal);

    private ChannelHistory Read(string channel)
    {
        if (!File.Exists(HistoryOf(channel)))
        {
            return File.Exists(Path.Combine(root, channel + ".json"))
                ? throw new InvalidOperationException($"history/{channel}.json is gone while {channel} serves: no lever starts it over.")
                : new ChannelHistory([]);
        }

        return ChannelHistory.Open(File.ReadAllBytes(HistoryOf(channel)), release, backup)
            ?? throw new InvalidOperationException($"history/{channel}.json is signed by neither of thermi's keys, so no lever signs over it.");
    }

    /// <summary>
    /// Why <paramref name="key"/> may not seal what a lever changed, or null: each new manifest, sealed, must open as a
    /// PC opens it, so a channel whose release key is revoked takes only the backup key's signature.
    /// </summary>
    public string? Refuses(IReadOnlyDictionary<string, ChannelHistory> before, IReadOnlyDictionary<string, ChannelHistory> after, ECDsa key)
    {
        foreach (var (channel, history) in after)
        {
            if (history.Current is { } current && !ReferenceEquals(before[channel].Current, current)
                && ManifestEnvelope.Open(ManifestEnvelope.Seal(current, key), release, backup).Problem is { } problem)
            {
                return current.RevokedKey is not null
                    ? $"{channel}'s release key is revoked: sign with the backup key until PCs run a build with the new one, then unrevoke."
                    : $"{channel}'s next manifest would not open on a PC: {problem}.";
            }
        }

        return null;
    }

    /// <summary>Writes each channel whose history changed, its history first and its envelope last, sealed by <paramref name="key"/>.</summary>
    public IReadOnlyList<Manifest> Write(IReadOnlyDictionary<string, ChannelHistory> before, IReadOnlyDictionary<string, ChannelHistory> after, ECDsa key)
    {
        var written = new List<Manifest>();
        foreach (var (channel, history) in after)
        {
            if (history.Current is not { } current || ReferenceEquals(before[channel].Current, current))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(HistoryOf(channel))!);
            File.WriteAllBytes(HistoryOf(channel), history.Seal(key));
            File.WriteAllBytes(Path.Combine(root, channel + ".json"), ManifestEnvelope.Seal(current, key));
            written.Add(current);
        }

        return written;
    }

    /// <summary>
    /// Copies a processor's zip of <paramref name="version"/> into the folder, where it is served from
    /// <paramref name="baseUrl"/>: a zip already there must be the same bytes, since a version's zips never change.
    /// </summary>
    public Package Add(ReleaseVersion version, string processor, string zip, Uri baseUrl)
    {
        var name = $"zips/{version}/thermi-{version}-{processor}.zip";
        var target = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(zip);
        if (File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
        {
            throw new InvalidOperationException($"{name} was published with other bytes: a version's zips never change.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
        return new Package(new Uri(baseUrl, name), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private string HistoryOf(string channel) => Path.Combine(root, "history", channel + ".json");
}
