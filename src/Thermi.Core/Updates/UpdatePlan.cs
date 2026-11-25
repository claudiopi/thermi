using System.Globalization;

namespace Thermi.Core.Updates;

public enum PlanKind
{
    /// <summary>Nothing: the PC runs what the channel offers it, or may not take it.</summary>
    Nothing,

    /// <summary>The manifest is of another channel, older than the last one taken or expired: it is ignored, sequence and all.</summary>
    Refuse,

    /// <summary>Download, check and unpack <see cref="Plan.Version"/>, replacing any version ready before it.</summary>
    Download,

    /// <summary>The version ready already is the one offered; <see cref="Plan.Urgent"/> says whether it is urgent now.</summary>
    Keep,

    DropReady,

    /// <summary>The installed version is withdrawn: switch back to the kept <see cref="Plan.Version"/>.</summary>
    RollBack,
}

public sealed record Plan(PlanKind Kind, ReleaseVersion? Version = null, Package? Package = null, bool Urgent = false, string? Refused = null);

/// <summary>What became of a download a plan asked for.</summary>
public enum Fetched
{
    /// <summary>Not fetched, or refused for the PC's reasons, as a network down or a folder in use: the next check tries again.</summary>
    Later,

    /// <summary>Checked and unpacked: the version is ready.</summary>
    Ready,

    /// <summary>Refused for what the zip holds, which fetching it again cannot change.</summary>
    Refused,
}

/// <summary>
/// What the service takes from a channel's manifest, given what it runs and keeps. It never goes below or to the installed
/// version, nor to one that failed here; a withdrawn installed version goes back only to the previous version this PC ran
/// and kept, never to one it would download. A PC takes a version only when its rollout number is below the percentage.
/// </summary>
public static class UpdatePlan
{
    // A PC's clock a day behind still takes a manifest signed today.
    /// <summary>How far a PC's clock may be off before a time it wrote, or a server's, is taken for a wrong clock's.</summary>
    public static readonly TimeSpan ClockSlack = TimeSpan.FromDays(1);

    /// <summary>
    /// What to do with <paramref name="read"/>'s manifest at <paramref name="now"/>, on a build whose release key's SHA-256
    /// is <paramref name="releaseKeyHash"/>. The backup key's revocation of that key is taken whatever its sequence, once,
    /// since that key may have pushed the sequence out of reach.
    /// </summary>
    public static Plan Decide(UpdateState state, ManifestRead read, string channel, string processor, DateTimeOffset now, string? releaseKeyHash = null)
    {
        var manifest = read.Manifest ?? throw new ArgumentException("A manifest that was refused plans nothing.", nameof(read));
        if (Refusal(state, read, channel, now, releaseKeyHash) is { } refused)
        {
            return new Plan(PlanKind.Refuse, Refused: refused);
        }

        if (state.Installed is not { } installed)
        {
            return new Plan(PlanKind.Nothing);
        }

        if (manifest.Withdrawn.Contains(installed))
        {
            return state.Previous is { } previous && previous < installed && !manifest.Withdrawn.Contains(previous) && !state.Failed.Contains(previous)
                ? new Plan(PlanKind.RollBack, previous)
                : Dropped(state);
        }

        var offered = Offered(state, manifest, installed, processor);
        if (offered is null)
        {
            return Dropped(state);
        }

        return state.Ready == offered.Version ? offered with { Kind = PlanKind.Keep, Package = null } : offered;
    }

    /// <summary>
    /// The state a check leaves once <paramref name="plan"/> was carried out by <paramref name="self"/> at
    /// <paramref name="now"/>, <paramref name="fetched"/> saying what became of its download, and the version to switch to
    /// now, if any: one made ready switches at once only when urgent, and forward only while no hold stops it; a rollback
    /// goes back whatever holds.
    /// </summary>
    public static (UpdateState State, ReleaseVersion? SwitchTo) Apply(UpdateState state, Plan plan, Fetched fetched, ReleaseVersion self, DateTimeOffset now)
    {
        var (next, to) = Carried(state, plan, fetched);
        return (next, to is { } target && (target < self || SwitchHealth.MaySwitch(next, now)) ? target : null);
    }

    private static (UpdateState State, ReleaseVersion? SwitchTo) Carried(UpdateState state, Plan plan, Fetched fetched)
    {
        if (plan is { Kind: PlanKind.Download, Version: { } version, Package: { } package })
        {
            return Downloaded(state, version, package, plan.Urgent, fetched);
        }

        return plan.Kind == PlanKind.Keep ? (state with { ReadyUrgent = plan.Urgent }, plan.Urgent ? state.Ready : null)
            : plan.Kind == PlanKind.DropReady ? (state with { Ready = null, ReadyUrgent = false }, null)
            : plan.Kind == PlanKind.RollBack ? (state, plan.Version)
            : (state, null);
    }

    private static (UpdateState State, ReleaseVersion? SwitchTo) Downloaded(
        UpdateState state, ReleaseVersion version, Package package, bool urgent, Fetched fetched) =>
        fetched == Fetched.Ready ? (state with { Ready = version, ReadyUrgent = urgent, Refused = null }, urgent ? version : null)
            : fetched == Fetched.Refused ? (state with { Refused = package.Sha256 }, null)
            : (state, null);

    /// <summary>
    /// Whether <paramref name="read"/> revokes this build's release key now: signed by the backup key, naming that key, and
    /// not revoked here yet. A revocation of another key, or one replayed, changes nothing.
    /// </summary>
    public static bool Revokes(UpdateState state, ManifestRead read, string? releaseKeyHash) =>
        read is { Backup: true, Manifest.RevokedKey: { } revoked } && revoked == releaseKeyHash && state.RevokedKey != revoked;

    /// <summary>Why a well signed manifest is ignored, or null: of another channel, older than the last one taken, or expired.</summary>
    public static string? Refusal(UpdateState state, ManifestRead read, string channel, DateTimeOffset now, string? releaseKeyHash)
    {
        var manifest = read.Manifest!;
        var reset = Revokes(state, read, releaseKeyHash);
        return !string.Equals(manifest.Channel, channel, StringComparison.Ordinal) ? $"it is of the {manifest.Channel} channel"
            : manifest.Sequence < state.SequenceOf(channel) && !reset
                ? string.Create(CultureInfo.InvariantCulture, $"its sequence {manifest.Sequence} is older than {state.SequenceOf(channel)}")
            : now > manifest.Expires ? string.Create(CultureInfo.InvariantCulture, $"it expired on {manifest.Expires:u}")
            : manifest.Expires > now + Manifest.Lifetime + ClockSlack
                ? string.Create(CultureInfo.InvariantCulture, $"it expires on {manifest.Expires:u}, further ahead than a manifest may")
            : null;
    }

    private static Plan? Offered(UpdateState state, Manifest manifest, ReleaseVersion installed, string processor)
    {
        if (manifest.Disabled
            || manifest.Version is not { } version
            || version <= installed
            || state.Failed.Contains(version)
            || manifest.Withdrawn.Contains(version)
            || state.Bucket is < 0 or > 99
            || state.Bucket >= manifest.Rollout
            || !manifest.Packages.TryGetValue(processor, out var package)
            || string.Equals(state.Refused, package.Sha256, StringComparison.Ordinal))
        {
            return null;
        }

        return new Plan(PlanKind.Download, version, package, manifest.Urgent);
    }

    private static Plan Dropped(UpdateState state) => new(state.Ready is null ? PlanKind.Nothing : PlanKind.DropReady);
}
