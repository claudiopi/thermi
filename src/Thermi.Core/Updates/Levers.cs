using System.Text.Json;

namespace Thermi.Core.Updates;

/// <summary>Every manifest a channel published, oldest first: the last is what it serves, the rest are what a rollback reaches.</summary>
public sealed record ChannelHistory(IReadOnlyList<Manifest> Published)
{
    public const int Kept = 50;

    public Manifest? Current => Published.Count > 0 ? Published[^1] : null;

    public static ChannelHistory Read(string? text) =>
        text is null ? new ChannelHistory([]) : JsonSerializer.Deserialize(text, UpdateJson.Default.ChannelHistory) ?? new ChannelHistory([]);

    public string Write() => JsonSerializer.Serialize(this, UpdateJson.Default.ChannelHistory);

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
        IReadOnlyDictionary<string, ChannelHistory> channels, string channel, ReleaseVersion version, IReadOnlyDictionary<string, Package> packages,
        int rollout, bool urgent, DateTimeOffset now)
    {
        var current = channels[channel].Current;
        if (current?.Version is { } offered && version < offered)
        {
            return new LeverResult(channels, $"{channel} offers {offered}, above {version}: a channel never goes back; use rollback.");
        }

        if (current?.Version == version && !SamePackages(current.Packages, packages))
        {
            return new LeverResult(channels, $"{version} was published with other files: a version's zips never change; publish a new version.");
        }

        if (rollout is < 0 or > 100)
        {
            return new LeverResult(channels, "The rollout is a percentage, from 0 to 100.");
        }

        var next = Next(current, channel, now) with
        {
            Version = version,
            Packages = packages,
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
                Packages = packages,
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
            ? Publish(channels, to, version, source.Packages, rollout, urgent ?? source.Urgent, now)
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
        (current ?? new Manifest { Channel = channel }) with { Sequence = (current?.Sequence ?? 0) + 1, Expires = now + Manifest.Lifetime, Revokes = false };

    private static bool SamePackages(IReadOnlyDictionary<string, Package> one, IReadOnlyDictionary<string, Package> other) =>
        one.Count == other.Count && one.All(package => other.TryGetValue(package.Key, out var same) && same == package.Value);
}
