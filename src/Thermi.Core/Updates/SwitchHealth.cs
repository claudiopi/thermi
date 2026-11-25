namespace Thermi.Core.Updates;

public enum Verdict
{
    Waiting,
    Passed,
    Failed,
}

public enum StartStep
{
    /// <summary>Serve; put the launchers of the version it is back in place, should they be missing or another's.</summary>
    Serve,

    /// <summary>The version it is failed its probation: switch back to the previous one.</summary>
    RollBack,
}

/// <summary>
/// Whether a switch worked, judged only by what happened, counted in polls, ticks and starts that run only while Windows
/// is awake: a clock that ran while the PC slept would fail a version that never had the chance to start. Time only ever
/// ends a probation, never fails one. And what a service makes of a switch it finds written down as it starts: done when
/// it is the new version, cut when it is the old one.
/// </summary>
public static class SwitchHealth
{
    /// <summary>The watchdog's polls of the pipe, one a second, within which the new service must serve a snapshot.</summary>
    public const int ServicePolls = 120;

    /// <summary>The old overlay's ticks, one a second, within which the new one must come up.</summary>
    public const int OverlayTicks = 60;

    /// <summary>A switch cut this many times fails its version, as a version that cannot come up might cut it.</summary>
    public const int Cuts = 2;

    /// <summary>A second run of the new service ending without stopping, in this long, sends the PC back.</summary>
    public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(10);

    public static Verdict Service(int polls, bool served) =>
        served ? Verdict.Passed : polls >= ServicePolls ? Verdict.Failed : Verdict.Waiting;

    public static Verdict Overlay(int ticks, bool cameUp, bool exited) =>
        cameUp ? Verdict.Passed : exited || ticks >= OverlayTicks ? Verdict.Failed : Verdict.Waiting;

    public static UpdateState Begin(UpdateState state, ReleaseVersion to, bool back = false) => state with
    {
        Switch = new PendingSwitch(state.Installed ?? to, to, state.Switch is { } cut && cut.To == to ? cut.Cuts : 0, Running: true, back),
    };

    public static UpdateState Done(UpdateState state, DateTimeOffset now)
    {
        var pending = state.Switch ?? throw new InvalidOperationException("No switch was written down.");
        var rollback = pending.Back;
        return state with
        {
            Installed = pending.To,
            Previous = rollback ? null : pending.From,
            Refused = null,
            SwitchAfter = null,
            Ready = state.Ready == pending.To ? null : state.Ready,
            ReadyUrgent = state.Ready != pending.To && state.ReadyUrgent,
            InstalledUrgent = !rollback && state.Ready == pending.To && state.ReadyUrgent,
            Failed = rollback ? [.. state.Failed.Append(pending.From).Distinct()] : state.Failed,
            Switch = null,
            Probation = rollback ? null : new Probation(pending.From, now, Stopped: false),
        };
    }

    /// <summary>The state once the new version of a switch failed: the PC stays, and never takes it again.</summary>
    public static UpdateState Failed(UpdateState state)
    {
        var pending = state.Switch ?? throw new InvalidOperationException("No switch was written down.");
        return state with
        {
            Failed = [.. state.Failed.Append(pending.To).Distinct()],
            Ready = state.Ready == pending.To ? null : state.Ready,
            ReadyUrgent = state.Ready != pending.To && state.ReadyUrgent,
            Switch = null,
        };
    }

    /// <summary>
    /// The state and the step of a service that starts at <paramref name="now"/> as version <paramref name="self"/>: a switch
    /// left running is done when it is the new version and cut when it is the old one; a run before it that never stopped
    /// counts as a crash while in <see cref="CrashWindow"/>, and the second fails the version.
    /// </summary>
    public static (UpdateState State, StartStep Step) Started(UpdateState state, ReleaseVersion self, DateTimeOffset now)
    {
        if (state.Switch is { Running: true } pending)
        {
            if (pending.To == self)
            {
                return (Done(state, now), StartStep.Serve);
            }

            state = Cut(state, pending);
        }

        return Probation(Adopted(state, self), now);
    }

    /// <summary>
    /// The state once <paramref name="self"/> was installed by hand, from an MSI or a script: the PC runs it, with no
    /// probation, and a version ready that is not later than it is dropped, or the PC would switch back down to it.
    /// </summary>
    public static UpdateState InstalledByHand(UpdateState state, ReleaseVersion self) => state with
    {
        Installed = self,
        Probation = null,
        Refused = null,
        SwitchAfter = null,
        Ready = state.Ready > self ? state.Ready : null,
        ReadyUrgent = state.Ready > self && state.ReadyUrgent,
    };

    /// <summary>The state once the service stopped as asked, which is never a crash.</summary>
    public static UpdateState Stopped(UpdateState state) =>
        state.Probation is { } probation ? state with { Probation = probation with { Stopped = true } } : state;

    /// <summary>
    /// A switch that went back without failing, which waits a check's interval: <paramref name="cut"/> when Windows gave up
    /// on its start, which also counts towards failing the version.
    /// </summary>
    public static UpdateState HeldBack(UpdateState state, bool cut, DateTimeOffset now) =>
        (cut ? Cut(state) : state with { Switch = null }) with { SwitchAfter = now + UpdateSchedule.Every };

    /// <summary>
    /// Whether a switch forward to the version ready may begin now: a hold further ahead than one interval and a day was set by a
    /// clock that ran ahead, and holds nothing.
    /// </summary>
    public static bool MaySwitch(UpdateState state, DateTimeOffset now) =>
        state.SwitchAfter is not { } after || after <= now || after - now > UpdateSchedule.Every + UpdatePlan.ClockSlack;

    /// <summary>
    /// The version ready that <paramref name="self"/> switches to at a quiet moment or About's restart, or null: only a
    /// later one, while <paramref name="self"/> is the version installed and no hold stops the switch.
    /// </summary>
    public static ReleaseVersion? ReadyFor(UpdateState state, ReleaseVersion self, DateTimeOffset now) =>
        state.Installed == self && state.Ready is { } ready && ready > self && MaySwitch(state, now) ? ready : null;

    private static UpdateState Cut(UpdateState state) => Cut(state, state.Switch ?? throw new InvalidOperationException("No switch was written down."));

    private static UpdateState Cut(UpdateState state, PendingSwitch pending)
    {
        var cut = pending with { Cuts = pending.Cuts + 1, Running = false };
        if (cut.Cuts < Cuts)
        {
            return state with { Switch = cut };
        }

        // A rollback cut twice says nothing against the version it goes back to, which ran here before.
        return pending.Back ? state with { Switch = null } : Failed(state with { Switch = cut });
    }

    // A service that starts as another version than the state says was installed by hand: the PC runs what it runs.
    private static UpdateState Adopted(UpdateState state, ReleaseVersion self) => state.Installed != self ? InstalledByHand(state, self) : state;

    private static (UpdateState State, StartStep Step) Probation(UpdateState state, DateTimeOffset now)
    {
        if (state.Probation is not { } probation || now - probation.At >= CrashWindow)
        {
            return (state with { Probation = null }, StartStep.Serve);
        }

        var next = probation with { Crashes = probation.Crashes + (probation.Stopped ? 0 : 1), Stopped = false };
        return next.Crashes >= 2 && state.Previous is { } previous && !state.Failed.Contains(previous)
            ? (state with { Probation = next }, StartStep.RollBack)
            : (state with { Probation = next }, StartStep.Serve);
    }
}
