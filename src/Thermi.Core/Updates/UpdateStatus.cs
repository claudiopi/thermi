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

    NotChecked,
}

public sealed record UpdateLine(UpdateLineKind Kind, bool Beta, ReleaseVersion? Version = null, DateTimeOffset? At = null);

/// <summary>The one line About shows on updates; nothing else ever shows one, since an update is meant to go unseen.</summary>
public static class UpdateStatus
{
    /// <summary>How long checks may fail before About says so: long enough for a trip, short enough to notice a proxy.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(14);

    public static UpdateLine Of(UpdatePolicy policy, UpdateState state, DateTimeOffset now)
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

        if (state.Ready is { } ready)
        {
            return new UpdateLine(UpdateLineKind.Ready, beta, ready);
        }

        if ((state.LastSuccess ?? state.FirstCheck) is { } since && now - since >= Stale)
        {
            return new UpdateLine(UpdateLineKind.Unreached, beta, At: since);
        }

        return state.LastSuccess is { } last
            ? new UpdateLine(UpdateLineKind.UpToDate, beta, At: last)
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
            _ => throw new ArgumentOutOfRangeException(nameof(line), line.Kind, null),
        };
        return line.Beta ? texts.Format(numbers, WindowText.UpdatesBeta, said) : said;
    }
}
