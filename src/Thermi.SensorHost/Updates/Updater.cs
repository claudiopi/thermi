using System.Diagnostics;
using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// The service's updater (docs/adr/0008-updates.md): a minute after the service starts, every four hours and after a
/// resume, it reads the channel's manifest, and downloads, checks and unpacks the version it offers, which then waits for
/// a quiet moment. It decides nothing itself: Core's <see cref="UpdatePlan"/> and <see cref="SwitchHealth"/> do. Nothing
/// it meets ever stops the service: every failure is written to the event log and tried again at the next check.
/// </summary>
internal sealed class Updater(UpdateBuild build, InstallFolder folder, ManifestClient client, TimeProvider time, Action<string, EventLogEntryType> say)
    : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly Switcher _switcher = new(folder, time, say);
    private readonly SemaphoreSlim _busy = new(1, 1);
    private volatile bool _checkSoon;
    private volatile bool _switchSoon;
    private volatile bool _goBack;
    private DateTimeOffset _lastQuiet = DateTimeOffset.MinValue;

    /// <summary>
    /// At the service's start: finishes or undoes a switch left written down, counts a run that never stopped and puts the
    /// launchers of this version back, before the state says which version runs, so an overlay that reads it starts this
    /// one. A version that failed its probation goes back, and a version ready switches, once the service has started.
    /// </summary>
    public void Started()
    {
        if (_switcher.UnderWay() || !Trusted())
        {
            return;
        }

        var (state, step) = SwitchHealth.Started(folder.Read(), build.Version, time.GetUtcNow());
        if (folder.PlaceLaunchers(build.Version))
        {
            say($"thermi put back the launchers of {build.Version}.", EventLogEntryType.Information);
        }

        folder.Write(state);
        if (step == StartStep.RollBack)
        {
            say(
                $"thermi {build.Version} ended without stopping twice since it was installed, so the PC goes back to {state.Previous}.",
                EventLogEntryType.Warning);
        }

        _goBack = step == StartStep.RollBack;
        _switchSoon = _goBack || state.Ready is not null;
    }

    /// <summary>At a stop asked for, which is never a crash.</summary>
    public void Stopped()
    {
        if (!_switcher.UnderWay() && Trusted())
        {
            folder.Write(SwitchHealth.Stopped(folder.Read()));
        }
    }

    public void Resumed()
    {
        if (UpdateSchedule.DueOnResume(time.GetUtcNow(), folder.Read().LastCheck))
        {
            _checkSoon = true;
        }
    }

    /// <summary>Code 128: an overlay's session is quiet, so a ready version may switch now; at most once a minute.</summary>
    public void Quiet()
    {
        var now = time.GetUtcNow();
        if (now - _lastQuiet < UpdateSchedule.QuietEvery || !_busy.Wait(0))
        {
            return;
        }

        _lastQuiet = now;
        try
        {
            SwitchReady();
        }
        finally
        {
            _busy.Release();
        }
    }

    public async Task RunAsync(CancellationToken stop)
    {
        if (!build.Built)
        {
            return;
        }

        var due = time.GetUtcNow() + UpdateSchedule.FirstCheck;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(Tick, time, stop);
                if (_switchSoon)
                {
                    _switchSoon = false;
                    await SwitchOnStartAsync(stop);
                }
                else if (time.GetUtcNow() >= due || _checkSoon)
                {
                    _checkSoon = false;
                    due = time.GetUtcNow() + UpdateSchedule.Every;
                    await CheckAsync(stop);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // Stopping is the normal way out.
        }
    }

    public async Task CheckAsync(CancellationToken stop)
    {
        await _busy.WaitAsync(stop);
        try
        {
            var policy = build.Policy();
            if (policy.Checks && !_switcher.UnderWay() && Trusted())
            {
                await CheckAsync(policy.Channel, stop);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested) // Logged; the next check tries again.
        {
            say($"thermi could not check for updates: {ex.Message}", EventLogEntryType.Warning);
        }
        finally
        {
            _busy.Release();
        }
    }

    public void Dispose() => _busy.Dispose();

    private async Task CheckAsync(string channel, CancellationToken stop)
    {
        var now = time.GetUtcNow();
        var state = folder.Read();
        if (state.Installed != build.Version)
        {
            return;
        }

        state = state with
        {
            Bucket = state.Bucket is >= 0 and <= 99 ? state.Bucket : RandomNumberGenerator.GetInt32(100),
            FirstCheck = state.FirstCheck ?? now,
            LastCheck = now,
        };
        var envelope = await client.EnvelopeAsync(build.ManifestOf(channel), stop);
        var read = envelope is null ? null : ManifestEnvelope.Open(envelope, build.ReleaseKeyHash == state.RevokedKey ? null : build.Release, build.Backup);
        var plan = read?.Manifest is null ? null : UpdatePlan.Decide(state, read, channel, build.Processor, now);
        if (read?.Manifest is not { } manifest || plan is null || plan.Kind == PlanKind.Refuse)
        {
            say($"thermi refused the {channel} channel's manifest: {Refusal(envelope, read, plan)}.", EventLogEntryType.Warning);
            folder.Write(state);
            return;
        }

        state = state.WithSequence(channel, manifest.Sequence) with
        {
            LastSuccess = now,
            RevokedKey = read.Backup && manifest.RevokedKey is { } revoked && revoked == build.ReleaseKeyHash ? revoked : state.RevokedKey,
        };
        var (next, switchTo) = await ApplyAsync(state, plan, stop);

        // A stop while the check ran wrote the probation; the check's own state never takes it back.
        next = next with { Probation = folder.Read().Probation };
        folder.Write(next);
        Prune(next);
        if (switchTo is { } to)
        {
            _switcher.Begin(next, to, build.Version);
        }
    }

    private async Task<(UpdateState State, ReleaseVersion? SwitchTo)> ApplyAsync(UpdateState state, Plan plan, CancellationToken stop)
    {
        switch (plan.Kind)
        {
            case PlanKind.Download:
                return plan is { Version: { } version, Package: { } package }
                    && UpdateSchedule.MayDownload(Network.Metered(), plan.Urgent) && await FetchAsync(version, package, stop)
                    ? (state with { Ready = version, ReadyUrgent = plan.Urgent }, plan.Urgent ? version : null)
                    : (state, null);
            case PlanKind.Keep:
                return (state with { ReadyUrgent = plan.Urgent }, plan.Urgent ? state.Ready : null);
            case PlanKind.DropReady:
                say($"thermi {state.Ready} is no longer offered, so it is deleted.", EventLogEntryType.Information);
                return (state with { Ready = null, ReadyUrgent = false }, null);
            case PlanKind.RollBack:
                say($"thermi {state.Installed} was withdrawn, so the PC goes back to {plan.Version}.", EventLogEntryType.Warning);
                return (state, plan.Version);
            case PlanKind.Nothing:
            case PlanKind.Refuse:
            default:
                return (state, null);
        }
    }

    private async Task<bool> FetchAsync(ReleaseVersion version, Package package, CancellationToken stop)
    {
        if (build.Catalog)
        {
            // A release build takes only files its signed catalog lists, which this build cannot check yet.
            say($"thermi refused {version}: this build cannot check its catalog. Nothing installed changed.", EventLogEntryType.Warning);
            return false;
        }

        var download = await client.DownloadAsync(package, folder, stop);
        await using var zip = download.Zip;
        var refused = zip is null ? download.Refused : Unpacker.Unpack(zip, folder, version, catalog: null);
        if (refused is not null)
        {
            say($"thermi refused {version}: {refused}. Nothing installed changed.", EventLogEntryType.Warning);
            return false;
        }

        say($"thermi {version} is ready, and switches when the PC is quiet.", EventLogEntryType.Information);
        return true;
    }

    // A version ready switches when a quiet overlay says so, or when the service starts; a PC no one signs in to has no overlay.
    private void SwitchReady()
    {
        var state = folder.Read();
        if (build.Policy().Checks && !_switcher.UnderWay() && state.Installed == build.Version && state.Ready is { } ready && Trusted())
        {
            _switcher.Begin(state, ready, build.Version);
        }
    }

    // After OnStart returned, so the watchdog may stop the service: a failed probation goes back, else a version ready switches.
    private async Task SwitchOnStartAsync(CancellationToken stop)
    {
        await _busy.WaitAsync(stop);
        try
        {
            var state = folder.Read();
            if (_goBack && state.Previous is { } previous)
            {
                if (!_switcher.UnderWay() && Trusted())
                {
                    _switcher.Begin(state, previous, build.Version, back: true);
                }
            }
            else
            {
                SwitchReady();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stop.IsCancellationRequested) // As a check: logged, never the updater's end.
        {
            say($"thermi could not switch as it started: {ex.Message}", EventLogEntryType.Warning);
        }
        finally
        {
            _busy.Release();
        }
    }

    private void Prune(UpdateState state)
    {
        HashSet<ReleaseVersion> keep = [build.Version];
        foreach (var version in new[] { state.Installed, state.Previous, state.Ready })
        {
            if (version is not null)
            {
                keep.Add(version);
            }
        }

        foreach (var removed in folder.Prune(keep))
        {
            say($"thermi deleted {removed}, which it no longer keeps.", EventLogEntryType.Information);
        }
    }

    private bool Trusted()
    {
        if (folder.Trusted())
        {
            return true;
        }

        say($"{folder.Top} is not a folder only administrators change, so thermi does not update.", EventLogEntryType.Error);
        return false;
    }

    private static string Refusal(byte[]? envelope, ManifestRead? read, Plan? plan) =>
        envelope is null ? "it could not be read"
            : read?.Problem is { } problem ? problem switch
            {
                ManifestProblem.TooLong => "it is longer than a manifest may be",
                ManifestProblem.NotSigned => "no key thermi trusts signed it",
                ManifestProblem.Malformed => "it is not a manifest",
                ManifestProblem.Invalid => "it breaks a manifest's rules",
                _ => "it breaks a manifest's rules",
            }
            : plan?.Refused ?? "it was refused";
}
