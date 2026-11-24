using System.Diagnostics;
using System.Security.Cryptography;
using Thermi.Core.Updates;
using Thermi.Trust;

namespace Thermi.SensorHost.Updates;

/// <summary>
/// The service's updater (docs/adr/0008-updates.md): a minute after the service starts, every four hours and after a
/// resume, it reads the channel's manifest, and downloads, checks and unpacks the version it offers, which then waits for
/// a quiet moment. It decides nothing itself: Core's <see cref="UpdatePlan"/> and <see cref="SwitchHealth"/> do. Nothing
/// it meets ever stops the service: every failure is written to the event log and tried again at the next check.
/// </summary>
internal sealed class Updater(
    UpdateBuild build, InstallFolder folder, ManifestClient client, TimeProvider time, Action<string, EventLogEntryType> say, UpdateHost? host = null)
    : IDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan AfterStart = TimeSpan.FromMinutes(1);

    private readonly UpdateHost _host = host ?? UpdateHost.Windows;
    private readonly Switcher _switcher = new(folder, time, say, (host ?? UpdateHost.Windows).Start);
    private readonly SemaphoreSlim _busy = new(1, 1);
    private volatile bool _checkSoon;
    private volatile bool _switchSoon;
    private DateTimeOffset _lastQuiet = DateTimeOffset.MinValue;

    /// <summary>
    /// At the service's start: finishes or undoes a switch left written down, counts a run that never stopped and puts the
    /// launchers of this version back, as each check does again while a program holds one. A version that failed its
    /// probation goes back at once, its watchdog waiting for the service to run; a version ready switches a minute after
    /// the service started.
    /// </summary>
    public void Started()
    {
        if (_switcher.UnderWay() || !Trusted())
        {
            return;
        }

        var (state, step) = SwitchHealth.Started(folder.Read(), build.Version, time.GetUtcNow());
        PlaceLaunchers();

        folder.Write(state);
        if (step == StartStep.RollBack)
        {
            say(
                $"thermi {build.Version} ended without stopping twice since it was installed, so the PC goes back to {state.Previous}.",
                EventLogEntryType.Warning);
        }

        // Back at once, from the start itself: a version that ends within seconds of every start would never reach a later
        // tick. The watchdog waits for the service to run before it stops it.
        if (step == StartStep.RollBack && state.Previous is { } previous)
        {
            _switcher.Begin(state, previous, build.Version, back: true);
            return;
        }

        _switchSoon = state.Ready is not null;
    }

    private void PlaceLaunchers()
    {
        try
        {
            if (folder.PlaceLaunchers(build.Version))
            {
                say($"thermi put back the launchers of {build.Version}.", EventLogEntryType.Information);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            say($"thermi could not put back the launchers of {build.Version}, and tries at its next check: {ex.Message}", EventLogEntryType.Warning);
        }
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
                // A switch the start asked for waits a minute, for a boot's disk and antivirus to settle.
                await Task.Delay(_switchSoon ? AfterStart : Tick, time, stop);
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
            var policy = _host.Policy(build);
            if (policy.Checks && !_switcher.UnderWay() && Trusted())
            {
                // A launcher a program held as a switch finished is put back here, or overlays would start the old one.
                PlaceLaunchers();
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
        var envelope = await EnvelopeAsync(state, channel, stop);
        var read = envelope is null ? null : ManifestEnvelope.Open(envelope, build.ReleaseKeyHash == state.RevokedKey ? null : build.Release, build.Backup);
        var plan = read?.Manifest is null ? null : UpdatePlan.Decide(state, read, channel, build.Processor, now, build.ReleaseKeyHash);
        if (read?.Manifest is not { } manifest || plan is null || plan.Kind == PlanKind.Refuse)
        {
            say($"thermi refused the {channel} channel's manifest: {Refusal(envelope, read, plan)}.", EventLogEntryType.Warning);
            WriteAfterCheck(state);
            return;
        }

        state = state.WithSequence(channel, manifest.Sequence) with
        {
            LastSuccess = now,
            RevokedKey = UpdatePlan.Revokes(state, read, build.ReleaseKeyHash) ? manifest.RevokedKey : state.RevokedKey,
        };
        var (next, switchTo) = await ApplyAsync(state, plan, stop);
        next = WriteAfterCheck(next);
        if (switchTo is { } to && (to < build.Version || SwitchHealth.MaySwitch(next, now)))
        {
            // From the state as it is now: a stop's mark written since the check's own write is kept.
            _switcher.Begin(folder.Read(), to, build.Version, back: to < build.Version);
        }

        Prune(next);
    }

    // A check that could not reach the channel still counts, so About tells a PC that never could.
    private async Task<byte[]?> EnvelopeAsync(UpdateState state, string channel, CancellationToken stop)
    {
        try
        {
            return await client.EnvelopeAsync(build.ManifestOf(channel), stop);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !stop.IsCancellationRequested)
        {
            WriteAfterCheck(state);
            throw;
        }
    }

    // A stop while the check ran wrote the probation; the check's own state never takes it back.
    private UpdateState WriteAfterCheck(UpdateState state)
    {
        var written = state with { Probation = folder.Read().Probation };
        folder.Write(written);
        return written;
    }

    private async Task<(UpdateState State, ReleaseVersion? SwitchTo)> ApplyAsync(UpdateState state, Plan plan, CancellationToken stop)
    {
        switch (plan.Kind)
        {
            case PlanKind.Download when plan is { Version: { } version, Package: { } package }:
                var metered = !UpdateSchedule.MayDownload(_host.Metered(), plan.Urgent);
                say(
                    metered ? $"thermi found {version}, and downloads it once the network is not metered." : $"thermi found {version}, and downloads it.",
                    EventLogEntryType.Information);
                return (metered ? Fetched.Later : await FetchAsync(version, package, stop)) switch
                {
                    Fetched.Ready => (state with { Ready = version, ReadyUrgent = plan.Urgent, Refused = null }, plan.Urgent ? version : null),
                    Fetched.Refused => (state with { Refused = package.Sha256 }, null),
                    Fetched.Later => (state, null),
                    _ => (state, null),
                };
            case PlanKind.Keep:
                return (state with { ReadyUrgent = plan.Urgent }, plan.Urgent ? state.Ready : null);
            case PlanKind.DropReady:
                say($"thermi {state.Ready} is no longer offered, so it is deleted.", EventLogEntryType.Information);
                return (state with { Ready = null, ReadyUrgent = false }, null);
            case PlanKind.RollBack:
                say($"thermi {state.Installed} was withdrawn, so the PC goes back to {plan.Version}.", EventLogEntryType.Warning);
                return (state, plan.Version);
            case PlanKind.Download:
            case PlanKind.Nothing:
            case PlanKind.Refuse:
            default:
                return (state, null);
        }
    }

    // A refused zip stays refused, as a download again cannot change it; a failed download is retried at the next check.
    private async Task<Fetched> FetchAsync(ReleaseVersion version, Package package, CancellationToken stop)
    {
        Download download;
        try
        {
            download = await client.DownloadAsync(package, folder, stop);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException && !stop.IsCancellationRequested)
        {
            say($"thermi could not download {version}, and tries at the next check: {ex.Message}", EventLogEntryType.Warning);
            return Fetched.Later;
        }

        await using var zip = download.Zip;
        Unpacker.Refusal? refused;
        try
        {
            refused = zip is null ? new Unpacker.Refusal(download.Refused!, ForTheZip: false) : Unpacker.Unpack(zip, folder, version, Catalog());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refused = new Unpacker.Refusal(ex.Message, ForTheZip: false);
        }

        if (refused is not null)
        {
            say($"thermi refused {version}: {refused.Why}. Nothing installed changed.", EventLogEntryType.Warning);
            return refused.ForTheZip ? Fetched.Refused : Fetched.Later;
        }

        say($"thermi {version} is ready, and switches when the PC is quiet.", EventLogEntryType.Information);
        return Fetched.Ready;
    }

    private enum Fetched
    {
        Later,
        Ready,
        Refused,
    }

    // A release build takes a version only when thermi's certificate signed its catalog and the catalog lists every file.
    private Func<string, string?>? Catalog() => build.Signers is { Count: > 0 } signers
        ? unpacked => Authenticode.SignedBy(Path.Combine(unpacked, FileCatalog.Name), signers)
            ? FileCatalog.Lists(unpacked)
            : $"its {FileCatalog.Name} is not signed by thermi's certificate"
        : null;

    // A version ready switches when a quiet overlay says so, or when the service starts; a PC no one signs in to has no overlay.
    private void SwitchReady()
    {
        var state = folder.Read();
        if (_host.Policy(build).Checks && !_switcher.UnderWay() && state.Installed == build.Version && state.Ready is { } ready && ready > build.Version
            && SwitchHealth.MaySwitch(state, time.GetUtcNow()) && Trusted())
        {
            _switcher.Begin(state, ready, build.Version);
        }
    }

    // After OnStart returned, so the watchdog may stop the service.
    private async Task SwitchOnStartAsync(CancellationToken stop)
    {
        await _busy.WaitAsync(stop);
        try
        {
            SwitchReady();
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
        // The version the launcher at the top starts, which overlays and sign-ins still run while a program holds it.
        ReleaseVersion? atTop;
        try
        {
            atTop = folder.AtTop();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A launcher a program holds unreadable: nothing is pruned until it can be told which version it starts.
            _ = ex;
            return;
        }

        HashSet<ReleaseVersion> keep = [build.Version];
        foreach (var version in new[] { state.Installed, state.Previous, state.Ready, atTop })
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
