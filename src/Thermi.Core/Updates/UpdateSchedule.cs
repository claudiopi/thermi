namespace Thermi.Core.Updates;

/// <summary>
/// Whether the service checks at all, and which channel: only a build with a key checks, and an administrator's policy,
/// under <c>HKLM\SOFTWARE\Policies\thermi</c>, turns checks off or picks the beta channel.
/// </summary>
public sealed record UpdatePolicy(bool Built, bool TurnedOff, string Channel)
{
    public const string Key = @"SOFTWARE\Policies\thermi";

    public const string UpdatesValue = "Updates";

    public const string ChannelValue = "UpdateChannel";

    public bool Checks => Built && !TurnedOff;

    /// <summary>
    /// Whether a build pointed at <paramref name="from"/>, with the certificates' thumbprints <paramref name="signers"/>
    /// names, updates itself: only a test channel on this PC goes without the catalog, so a release whose signers went
    /// missing never updates rather than take any zip the manifest key alone signed.
    /// </summary>
    public static bool Updates(string? from, string? signers) =>
        Uri.TryCreate(from, UriKind.Absolute, out var address) && (Signers(signers).Length > 0 || address.IsLoopback);

    /// <summary>The thumbprints a build's <c>ThermiUpdatesSigners</c> lists, separated by semicolons.</summary>
    public static string[] Signers(string? signers) =>
        signers?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    /// <summary>The policy from the registry's values as read: <c>Updates</c> = 0 turns checks off, <c>UpdateChannel</c> = beta picks beta.</summary>
    public static UpdatePolicy Of(bool built, object? updates, object? channel) => new(
        built,
        updates is int off && off == 0,
        channel is string named && string.Equals(named.Trim(), Manifest.Beta, StringComparison.OrdinalIgnoreCase) ? Manifest.Beta : Manifest.Stable);
}

public static class UpdateSchedule
{
    /// <summary>The service's control code an overlay sends when its session is quiet: Windows lets any signed-in user send it.</summary>
    public const int QuietCode = 128;

    /// <summary>The service's control code About sends as it opens, for a check now.</summary>
    public const int CheckCode = 129;

    /// <summary>The code About's restart sends: a switch to the version ready now, as a quiet session's, without its wait.</summary>
    public const int RestartCode = 130;

    /// <summary>How often About asks, and the service heeds it, at most: reopening it never hammers the channel.</summary>
    public static readonly TimeSpan AskEvery = TimeSpan.FromMinutes(1);

    /// <summary>How often an overlay says so, and the service heeds it, at most.</summary>
    public static readonly TimeSpan QuietEvery = TimeSpan.FromMinutes(1);

    /// <summary>After the service starts: long enough to stay out of Windows' own start.</summary>
    public static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Every = TimeSpan.FromHours(4);

    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(3);

    public static bool DueOnResume(DateTimeOffset now, DateTimeOffset? lastCheck) => lastCheck is not { } last || now - last >= Every;

    /// <summary>On a metered network only an urgent version downloads; the check itself is a few hundred bytes.</summary>
    public static bool MayDownload(bool metered, bool urgent) => !metered || urgent;

    /// <summary>
    /// Whether a session lets a ready version switch now: never with the settings window open or a drag under way; then
    /// at once for an urgent version, else after <see cref="Idle"/> without input, with the session locked, or with every
    /// overlay hidden, when nobody sees the overlays change.
    /// </summary>
    public static bool Quiet(SessionFacts session, bool urgent) =>
        !session.WindowOpen && !session.Dragging && (urgent || session.SinceInput >= Idle || session.Locked || session.AllHidden);
}

public sealed record SessionFacts(bool WindowOpen, bool Dragging, TimeSpan SinceInput, bool Locked, bool AllHidden);
