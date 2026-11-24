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
    public const int QuietCode = 128;

    private static readonly TimeSpan QuietEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly Switcher _switcher = new(folder, time, say);
    private readonly SemaphoreSlim _busy = new(1, 1);
    private volatile bool _checkSoon;
    private DateTimeOffset _lastQuiet = DateTimeOffset.MinValue;

    /// <summary>
    /// At the service's start: finishes or undoes a switch left written down, counts a run that never stopped, puts the
    /// launchers of this version back, and goes back to the previous version when this one failed its probation.
    /// </summary>
    public void Started()
    {
        if (Switcher.UnderWay() || !Trusted())
        {
            return;
        }

        var (state, step) = SwitchHealth.Started(folder.Read(), build.Version, time.GetUtcNow());
        folder.Write(state);
        if (folder.PlaceLaunchers(build.Version))
        {
            say($"thermi put back the launchers of {build.Version}.", EventLogEntryType.Information);
        }

        if (step == StartStep.RollBack && state.Previous is { } previous)
        {
            say($"thermi {build.Version} ended without stopping twice since it was installed, so the PC goes back to {previous}.", EventLogEntryType.Warning);
            _switcher.Begin(state, previous, build.Version);
        }
    }

    /// <summary>At a stop asked for, which is never a crash.</summary>
    public void Stopped()
    {
        if (!Switcher.UnderWay() && Trusted())
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
        if (now - _lastQuiet < QuietEvery || !_busy.Wait(0))
        {
            return;
        }

        _lastQuiet = now;
        try
        {
            var state = folder.Read();
            if (!Switcher.UnderWay() && state.Installed == build.Version && state.Ready is { } ready && Trusted())
            {
                _switcher.Begin(state, ready, build.Version);
            }
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
                if (time.GetUtcNow() >= due || _checkSoon)
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
            if (policy.Checks && !Switcher.UnderWay() && Trusted())
            {
                await CheckAsync(policy.Channel, stop);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or OperationCanceledException && !stop.IsCancellationRequested)
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
            RevokedKey = read.Backup && manifest.Revokes ? build.ReleaseKeyHash : state.RevokedKey,
        };
        var (next, switchTo) = await ApplyAsync(state, plan, stop);
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
