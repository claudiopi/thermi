using Thermi.Core.Translations;

namespace Thermi.Core.Updates;

public enum UpdateLineKind
{
    /// <summary>A build from the repo, which never checks.</summary>
    NotUpdating,

    TurnedOff,

    /// <summary>A version waits for a quiet moment.</summary>
    Ready,

    /// <summary>No check has worked for <see cref="UpdateStatus.Stale"/>, since <see cref="UpdateLine.At"/>.</summary>
    Unreached,

    UpToDate,

    /// <summary>Up to date, from a check within <see cref="UpdateSchedule.AskEvery"/>.</summary>
    CheckedNow,

    /// <summary>About asked for a check, which the service has not made yet.</summary>
    Checking,

    NotChecked,

    /// <summary>The system's package manager updates thermi, as on Linux, from the repository its packages added.</summary>
    FromSystem,
}

/// <summary><see cref="Restart"/>: the version ready may switch now, if asked.</summary>
public sealed record UpdateLine(UpdateLineKind Kind, bool Beta, ReleaseVersion? Version = null, DateTimeOffset? At = null, bool Restart = false);

/// <summary>The one line About shows on updates; nothing else ever shows one, since an update is meant to go unseen.</summary>
public static class UpdateStatus
{
    /// <summary>How long checks may fail before About says so: long enough for a trip, short enough to notice a proxy.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(14);

    /// <summary>How long About says it checks before it says what it knows, as a check that hangs would leave it.</summary>
    public static readonly TimeSpan CheckWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The line at <paramref name="now"/>, About having <paramref name="asked"/> for a check then: it checks until the
    /// service has checked since a minute before the ask, which the service would not have done again.
    /// </summary>
    public static UpdateLine Of(UpdatePolicy policy, UpdateState state, DateTimeOffset now, DateTimeOffset? asked = null)
    {
        var beta = policy.Channel == Manifest.Beta;
        if (!policy.Built)
        {
            return new UpdateLine(UpdateLineKind.NotUpdating, Beta: false);
        }

        if (policy.TurnedOff)
        {
            return new UpdateLine(UpdateLineKind.TurnedOff, beta);
        }

        if (asked is { } ask && now >= ask && now - ask < CheckWait && (state.LastCheck is not { } check || check < ask - UpdateSchedule.AskEvery))
        {
            return new UpdateLine(UpdateLineKind.Checking, beta);
        }

        if (state.Ready is { } ready)
        {
            return new UpdateLine(UpdateLineKind.Ready, beta, ready, Restart: SwitchHealth.MaySwitch(state, now));
        }

        if ((state.LastSuccess ?? state.FirstCheck) is { } since && now - since >= Stale)
        {
            return new UpdateLine(UpdateLineKind.Unreached, beta, At: since);
        }

        return state.LastSuccess is { } last
            ? new UpdateLine(now - last < UpdateSchedule.AskEvery ? UpdateLineKind.CheckedNow : UpdateLineKind.UpToDate, beta, At: last)
            : new UpdateLine(UpdateLineKind.NotChecked, beta);
    }

    public static string Say(UpdateLine line, Texts texts, IFormatProvider numbers, TimeZoneInfo zone)
    {
        var date = line.At is { } at ? TimeZoneInfo.ConvertTime(at, zone).ToString("d", numbers) : "";
        var said = line.Kind switch
        {
            UpdateLineKind.NotUpdating => texts.Of(WindowText.UpdatesNotUpdating),
            UpdateLineKind.TurnedOff => texts.Of(WindowText.UpdatesTurnedOff),
            UpdateLineKind.Ready => texts.Format(numbers, WindowText.UpdatesReady, line.Version),
            UpdateLineKind.Unreached => texts.Format(numbers, WindowText.UpdatesUnreached, date),
            UpdateLineKind.UpToDate => texts.Format(numbers, WindowText.UpdatesUpToDate, date),
            UpdateLineKind.NotChecked => texts.Of(WindowText.UpdatesNotChecked),
            UpdateLineKind.Checking => texts.Of(WindowText.UpdatesChecking),
            UpdateLineKind.CheckedNow => texts.Of(WindowText.UpdatesCheckedNow),
            UpdateLineKind.FromSystem => texts.Of(WindowText.UpdatesFromSystem),
            _ => throw new ArgumentOutOfRangeException(nameof(line), line.Kind, null),
        };
        return line.Beta ? texts.Format(numbers, WindowText.UpdatesBeta, said) : said;
    }
}
