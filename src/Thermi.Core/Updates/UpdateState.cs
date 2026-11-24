using System.Text.Json;

namespace Thermi.Core.Updates;

/// <summary>
/// A switch still on probation: the version it came from, when, and how many of the new service's runs ended without
/// stopping, counted at each start, with whether the one now running has stopped yet.
/// </summary>
public sealed record Probation(ReleaseVersion From, DateTimeOffset At, int Crashes = 0, bool Stopped = true);

/// <summary>
/// A switch written down before it starts, so a service that starts with it still there can tell whether it was done,
/// from the version it is: whether it is under way, and how many times one to the same version was cut before it was done.
/// </summary>
public sealed record PendingSwitch(ReleaseVersion From, ReleaseVersion To, int Cuts = 0, bool Running = false);

/// <summary>
/// What the service knows of its updates, in <c>update\state.json</c>, which only it writes and the overlay and the window
/// read: the versions installed, kept and ready, the ones that failed here, the last sequence taken of each channel, the
/// PC's rollout number, its checks, and a switch on probation.
/// </summary>
public sealed record UpdateState
{
    public const string FileName = "state.json";

    /// <summary>The version the launchers start.</summary>
    public ReleaseVersion? Installed { get; init; }

    /// <summary>The version before it, kept whole for a rollback.</summary>
    public ReleaseVersion? Previous { get; init; }

    /// <summary>A version downloaded, checked and unpacked, waiting for its switch.</summary>
    public ReleaseVersion? Ready { get; init; }

    public bool ReadyUrgent { get; init; }

    public IReadOnlyList<ReleaseVersion> Failed { get; init; } = [];

    public IReadOnlyDictionary<string, long> Sequences { get; init; } = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>The PC's number from 0 to 99 against a rollout, or -1 before it was drawn.</summary>
    public int Bucket { get; init; } = -1;

    /// <summary>The PC's first check, from which failing checks count when none ever worked.</summary>
    public DateTimeOffset? FirstCheck { get; init; }

    public DateTimeOffset? LastCheck { get; init; }

    public DateTimeOffset? LastSuccess { get; init; }

    public Probation? Probation { get; init; }

    public PendingSwitch? Switch { get; init; }

    /// <summary>A manifest signed by the backup key revoked the release key, so only the backup key is trusted.</summary>
    public bool Revoked { get; init; }

    /// <summary>The state <paramref name="text"/> holds; a file broken or missing is a PC that knows nothing yet. Never throws.</summary>
    public static UpdateState Read(string? text)
    {
        try
        {
            return text is null ? new UpdateState() : JsonSerializer.Deserialize(text, UpdateJson.Default.UpdateState) ?? new UpdateState();
        }
        catch (JsonException)
        {
            return new UpdateState();
        }
    }

    public string Write() => JsonSerializer.Serialize(this, UpdateJson.Default.UpdateState);

    public long SequenceOf(string channel) => Sequences.TryGetValue(channel, out var sequence) ? sequence : 0;

    public UpdateState WithSequence(string channel, long sequence) =>
        this with { Sequences = new Dictionary<string, long>(Sequences, StringComparer.Ordinal) { [channel] = sequence } };
}
