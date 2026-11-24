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

    /// <summary>The policy from the registry's values as read: <c>Updates</c> = 0 turns checks off, <c>UpdateChannel</c> = beta picks beta.</summary>
    public static UpdatePolicy Of(bool built, object? updates, object? channel) => new(
        built,
        updates is int off && off == 0,
        channel is string named && string.Equals(named.Trim(), Manifest.Beta, StringComparison.OrdinalIgnoreCase) ? Manifest.Beta : Manifest.Stable);
}

/// <summary>When the service checks, what it may download, and when a ready version may switch.</summary>
public static class UpdateSchedule
{
    /// <summary>After the service starts: long enough to stay out of Windows' own start.</summary>
    public static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan Every = TimeSpan.FromHours(4);

    /// <summary>How long a session must go without input to be quiet.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromMinutes(3);

    /// <summary>Whether a resume from sleep is a reason to check now: the last check is <see cref="Every"/> old, or none was made.</summary>
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

/// <summary>What a session's overlay knows of the moment: its window, a drag, the time since the last input, the lock and its overlays.</summary>
public sealed record SessionFacts(bool WindowOpen, bool Dragging, TimeSpan SinceInput, bool Locked, bool AllHidden);
