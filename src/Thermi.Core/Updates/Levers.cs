using System.Security.Cryptography;
using System.Text.Json;

namespace Thermi.Core.Updates;

/// <summary>
/// Every manifest a channel published, oldest first: the last is what it serves, the rest are what a rollback reaches.
/// Kept beside the channels and signed as a manifest is, so whoever can write the server cannot have a lever sign what
/// it planted there.
/// </summary>
public sealed record ChannelHistory(IReadOnlyList<Manifest> Published)
{
    public const int Kept = 50;

    public Manifest? Current => Published.Count > 0 ? Published[^1] : null;

    /// <summary>
    /// The history <paramref name="sealedOne"/> holds when the release or the backup key signed it; empty for none; null
    /// for one neither signed, or that is no history. An older one signed replays: a lever then signs a sequence PCs have
    /// passed, which they refuse, so it only withholds updates, as whoever holds the server can anyway.
    /// </summary>
    public static ChannelHistory? Open(byte[]? sealedOne, byte[] release, byte[] backup)
    {
        if (sealedOne is null)
        {
            return new ChannelHistory([]);
        }

        try
        {
            var envelope = JsonSerializer.Deserialize(sealedOne, UpdateJson.Default.Envelope);
            var body = Convert.FromBase64String(envelope?.Manifest ?? "");
            var signature = Convert.FromBase64String(envelope?.Signature ?? "");
            // A history holds at least one manifest, so a manifest's signed body, which shares the envelope, is none.
            return ManifestEnvelope.Verifies(release, body, signature) || ManifestEnvelope.Verifies(backup, body, signature)
                ? JsonSerializer.Deserialize(body, UpdateJson.Default.ChannelHistory) is { Published.Count: > 0 } history ? history : null
                : null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    public byte[] Seal(ECDsa key)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(this, UpdateJson.Default.ChannelHistory);
        var signature = key.SignData(body, HashAlgorithmName.SHA256);
        return JsonSerializer.SerializeToUtf8Bytes(new Envelope(Convert.ToBase64String(body), Convert.ToBase64String(signature)), UpdateJson.Default.Envelope);
    }

    public ChannelHistory With(Manifest manifest) => new([.. Published.Skip(Math.Max(0, Published.Count + 1 - Kept)), manifest]);
}

public sealed record LeverResult(IReadOnlyDictionary<string, ChannelHistory> Channels, string? Refused = null);

/// <summary>
/// The owner's levers on the channels, each a new manifest with the next sequence, as Mi Basket's are: publish a version,
/// promote beta's to stable, roll a channel back, turn it off. None ever offers a PC a version below what its channel
/// offered: a rollback withdraws the version instead, which sends only the PCs that run it back to what they kept.
/// </summary>
public static class Levers
{
    /// <summary>How many withdrawn versions a manifest carries: enough for every PC that ran one to see it.</summary>
    public const int WithdrawnKept = 10;

    public static LeverResult Publish(
        IReadOnlyDictionary<string, ChannelHistory> channels, string channel, ReleaseVersion version, VersionFiles files,
        int rollout, bool urgent, DateTimeOffset now)
    {
        var current = channels[channel].Current;
        if (current?.Version is { } offered && version < offered)
        {
            return new LeverResult(channels, $"{channel} offers {offered}, above {version}: a channel never goes back; use rollback.");
        }

        if (current?.Version == version && !(Same(current.Packages, files.Zips) && Same(current.Msis, files.Msis)))
        {
            return new LeverResult(channels, $"{version} was published with other files: a version's files never change; publish a new version.");
        }

        if (rollout is < 0 or > 100)
        {
            return new LeverResult(channels, "The rollout is a percentage, from 0 to 100.");
        }

        var next = Next(current, channel, now) with
        {
            Version = version,
            Packages = files.Zips,
            Msis = files.Msis,
            Rollout = rollout,
            Urgent = urgent,
            Disabled = false,
            Withdrawn = [.. (current?.Withdrawn ?? []).Where(withdrawn => withdrawn != version)],
        };
        var result = new Dictionary<string, ChannelHistory>(channels, StringComparer.Ordinal) { [channel] = channels[channel].With(next) };
        if (channel == Manifest.Stable && channels[Manifest.Beta].Current is var beta && (beta?.Version is null || beta.Version < version))
        {
            result[Manifest.Beta] = channels[Manifest.Beta].With(Next(beta, Manifest.Beta, now) with
            {
                Version = version,
                Packages = files.Zips,
                Msis = files.Msis,
                Rollout = 100,
                Urgent = urgent,
                Disabled = false,
            });
        }

        return new LeverResult(result);
    }

    /// <summary>Beta's version, files and all, to <paramref name="to"/>, without a rebuild.</summary>
    public static LeverResult Promote(
        IReadOnlyDictionary<string, ChannelHistory> channels, string from, string to, int rollout, bool? urgent, DateTimeOffset now)
    {
        return channels[from].Current is { Version: { } version } source
            ? Publish(channels, to, version, VersionFiles.Of(source), rollout, urgent ?? source.Urgent, now)
            : new LeverResult(channels, $"{from} offers no version to promote.");
    }

    /// <summary>Withdraws the channel's version and offers the one it offered before; PCs that run it go back to what they kept.</summary>
    public static LeverResult RollBack(IReadOnlyDictionary<string, ChannelHistory> channels, string channel, DateTimeOffset now)
    {
        var history = channels[channel];
        if (history.Current is not { Version: { } version } current)
        {
            return new LeverResult(channels, $"{channel} offers no version to roll back.");
        }

        var withdrawn = current.Withdrawn.Append(version).TakeLast(WithdrawnKept).ToList();
        var before = history.Published.Reverse().FirstOrDefault(manifest =>
            manifest.Version is { } earlier && earlier < version && !withdrawn.Contains(earlier));
        if (before is null)
        {
            return new LeverResult(channels, $"{channel} offered nothing before {version}: turn it off instead.");
        }

        var next = Next(current, channel, now) with
        {
            Version = before.Version,
            Packages = before.Packages,
            Msis = before.Msis,
            Rollout = 100,
            Urgent = false,
            Disabled = false,
            Withdrawn = withdrawn,
        };
        return new LeverResult(new Dictionary<string, ChannelHistory>(channels, StringComparer.Ordinal) { [channel] = history.With(next) });
    }

    /// <summary>Keeps every PC on the channel where it is: nothing is downloaded or switched until the next publish.</summary>
    public static LeverResult Disable(IReadOnlyDictionary<string, ChannelHistory> channels, string channel, DateTimeOffset now) =>
        Again(channels, channel, now, manifest => manifest with { Disabled = true });

    public static LeverResult Refresh(IReadOnlyDictionary<string, ChannelHistory> channels, string channel, DateTimeOffset now) =>
        Again(channels, channel, now, manifest => manifest);

    /// <summary>
    /// The channel's manifest again, naming the release key whose SHA-256 is <paramref name="keyHash"/> as revoked: only
    /// the backup key's signature makes PCs honor it, and they then trust the backup key alone until a build with another
    /// release key runs.
    /// </summary>
    public static LeverResult Revoke(IReadOnlyDictionary<string, ChannelHistory> channels, string channel, string keyHash, DateTimeOffset now) =>
        keyHash.Length == 64 && keyHash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            ? Again(channels, channel, now, manifest => manifest with { RevokedKey = keyHash })
            : new LeverResult(channels, "the key's SHA-256 is 64 lowercase hex digits");

    /// <summary>
    /// The channel's manifest again without its revocation, which every manifest after it carried for the PCs that missed
    /// one: once PCs run a build with the new release key.
    /// </summary>
    public static LeverResult Unrevoke(IReadOnlyDictionary<string, ChannelHistory> channels, string channel, DateTimeOffset now) =>
        Again(channels, channel, now, manifest => manifest with { RevokedKey = null });

    private static LeverResult Again(
        IReadOnlyDictionary<string, ChannelHistory> channels, string channel, DateTimeOffset now, Func<Manifest, Manifest> change)
    {
        var history = channels[channel];
        return history.Current is { } current
            ? new LeverResult(new Dictionary<string, ChannelHistory>(channels, StringComparer.Ordinal)
            {
                [channel] = history.With(change(Next(current, channel, now))),
            })
            : new LeverResult(channels, $"{channel} has published nothing.");
    }

    private static Manifest Next(Manifest? current, string channel, DateTimeOffset now) =>
        (current ?? new Manifest { Channel = channel }) with { Sequence = checked((current?.Sequence ?? 0) + 1), Expires = now + Manifest.Lifetime };

    private static bool Same(IReadOnlyDictionary<string, Package> one, IReadOnlyDictionary<string, Package> other) =>
        one.Count == other.Count && one.All(package => other.TryGetValue(package.Key, out var same) && same == package.Value);
}
