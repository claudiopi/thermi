extern alias sensors;

using System.Diagnostics;
using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.Windows.Tests.Updates;

/// <summary>
/// The service's updater against a channel served from this PC, on a fake clock: when it asks, what it sends, what it
/// takes and what it writes to the event log. Only the folder's access and the watchdog's start are the test's own.
/// </summary>
public sealed class UpdaterTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    // Keys built in, the policy's Updates and channel, a fresh install with no update folder yet, a metered network and
    // an urgent version; what the first check asks for, a minute after the start.
    [Theory]
    [InlineData(true, null, null, false, false, false, "stable zip")]
    [InlineData(true, 1, "beta", false, false, false, "beta zip")]
    [InlineData(true, 0, "beta", false, false, false, "")]
    [InlineData(false, null, null, false, false, false, "")]
    [InlineData(true, null, null, true, false, false, "stable zip")]
    [InlineData(true, null, null, false, true, false, "stable")]
    [InlineData(true, null, null, false, true, true, "stable zip")]
    public async Task ChecksFollowTheScheduleThePolicyAndTheNetwork(
        bool keys, int? updates, string? channel, bool fresh, bool metered, bool urgent, string asked)
    {
        await using var pc = new UpdatePc(fresh) { Policy = (updates, channel), Metered = metered };
        var zip = pc.Zip(UpdatePc.Built);
        pc.Publish(pc.Offer(UpdatePc.Built, zip) with { Urgent = urgent });
        pc.Publish(pc.Offer(UpdatePc.Built, zip) with { Channel = Manifest.Beta, Urgent = urgent });
        using var updater = pc.Updater(pc.Build(keys));
        using var stop = new CancellationTokenSource();
        updater.Started();
        var running = updater.RunAsync(stop.Token);

        if (!running.IsCompleted)
        {
            await pc.Clock.StepAsync(Minute - TimeSpan.FromSeconds(1));
            Assert.Empty(pc.Channel.Requests);
            pc.Clock.Advance(TimeSpan.FromSeconds(1));
            await pc.Clock.SettledAsync();
        }

        Assert.Equal(asked, Asked(pc));
        Assert.Equal(urgent && asked.Contains("zip", StringComparison.Ordinal), pc.Watchdogs.Count > 0);

        // Every four hours after; a resume an hour on asks nothing more, and a PC that slept past a check asks once as it wakes.
        var checks = Manifests(pc);
        await StepAsync(pc, running, UpdateSchedule.Every - Minute, minutes: true);
        Assert.Equal(checks, Manifests(pc));
        await StepAsync(pc, running, Minute);
        updater.Resumed();
        await StepAsync(pc, running, Minute);
        Assert.Equal(checks * 2, Manifests(pc));
        await StepAsync(pc, running, TimeSpan.FromHours(5));
        Assert.Equal(checks * 3, Manifests(pc));

        await stop.CancelAsync();
        await running;
    }

    // What a server, a cache or a user could make the service see; "nothing" is a version that passes every check.
    [Theory]
    [InlineData("nothing")]
    [InlineData("an envelope too long")]
    [InlineData("signed by another key")]
    [InlineData("of the beta channel")]
    [InlineData("an older sequence")]
    [InlineData("expired")]
    [InlineData("a zip longer than signed")]
    [InlineData("a zip of another hash")]
    [InlineData("an entry leaving its folder")]
    [InlineData("an older release relabelled")]
    [InlineData("a release build without its catalog")]
    [InlineData("a folder users write to")]
    [InlineData("the release key revoked")]
    public async Task TheUpdaterTakesOnlyWhatPassesEveryCheck(string wrong)
    {
        await using var pc = new UpdatePc();
        var installed = pc.Installed;
        var (build, trusted) = await ServeAsync(pc, wrong);
        using var updater = pc.Updater(build, trusted);
        var logged = pc.Events.Count;

        await updater.CheckAsync(TestContext.Current.CancellationToken);

        var taken = wrong == "nothing";
        Assert.Equal(installed, pc.Installed.Where(file => !file.StartsWith($"{UpdatePc.Built}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));
        Assert.Equal(taken ? UpdatePc.Built : null, pc.State.Ready);
        Assert.Equal(taken ? [UpdatePc.Running, UpdatePc.Built] : [UpdatePc.Running], pc.Versions);
        Assert.Equal(!taken, pc.Events.Skip(logged).Any(logged => logged.Type is EventLogEntryType.Warning or EventLogEntryType.Error));
    }

    [Fact]
    public async Task ACheckSendsNothingThatIdentifies()
    {
        await using var pc = new UpdatePc();
        pc.Publish(pc.Offer(UpdatePc.Running, pc.Zip(UpdatePc.Running)));
        using var updater = pc.Updater();

        // Twice, as a client that kept the server's cookie would send it back the second time.
        await updater.CheckAsync(TestContext.Current.CancellationToken);
        await updater.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, pc.Channel.Requests.Count);
        Assert.All(pc.Channel.Requests, request => Assert.Equal(
            ["GET /v1/stable.json HTTP/1.1", $"Host: {pc.Channel.Address.Authority}", "User-Agent: thermi"],
            request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)));
    }

    [Fact]
    public async Task EveryUpdateEventIsLogged()
    {
        await using var pc = new UpdatePc();
        var older = new ReleaseVersion(0, 7, 0);
        var zip = pc.Zip(UpdatePc.Built);
        using var updater = pc.Updater();

        // Found and downloaded; then urgent, so it switches; then withdrawn from under the PC, which goes back; then refused.
        Assert.Equal(2, await LoggedAsync(pc, updater, pc.Offer(UpdatePc.Built, zip)));
        Assert.Equal(1, await LoggedAsync(pc, updater, pc.Offer(UpdatePc.Built, zip, sequence: 3) with { Urgent = true }));
        pc.Folder().Write(new UpdateState { Installed = UpdatePc.Running, Previous = older, Ready = UpdatePc.Built });
        var withdrawn = pc.Offer(older, zip, sequence: 4) with { Withdrawn = [UpdatePc.Running] };
        Assert.Equal(2, await LoggedAsync(pc, updater, withdrawn));
        Assert.Equal(1, await LoggedAsync(pc, updater, withdrawn with { Sequence = 5, Expires = pc.Clock.GetUtcNow().AddMinutes(-1) }));

        // Each switch's watchdog: the running version's for a step forward, the older one's own for a step back.
        Assert.Equal(
            [pc.Folder().ServiceOf(UpdatePc.Running), pc.Folder().ServiceOf(older)],
            pc.Watchdogs);
    }

    private static async Task<int> LoggedAsync(UpdatePc pc, sensors::Thermi.SensorHost.Updates.Updater updater, Manifest manifest)
    {
        pc.Publish(manifest);
        var before = pc.Events.Count;
        await updater.CheckAsync(TestContext.Current.CancellationToken);
        return pc.Events.Count - before;
    }

    private static async Task<(sensors::Thermi.SensorHost.Updates.UpdateBuild Build, bool Trusted)> ServeAsync(UpdatePc pc, string wrong)
    {
        var zip = pc.Zip(UpdatePc.Built);
        var offer = pc.Offer(UpdatePc.Built, zip);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var relabelled = UpdatePc.Built with { Patch = UpdatePc.Built.Patch + 1 };
        var served = wrong switch
        {
            "an older sequence" => offer with { Sequence = 1 },
            "expired" => offer with { Expires = pc.Clock.GetUtcNow().AddMinutes(-1) },
            "a zip longer than signed" => offer with { Packages = X64(zip with { Size = zip.Size - 1 }) },
            "a zip of another hash" => offer with { Packages = X64(zip with { Sha256 = new string('0', 64) }) },
            "an entry leaving its folder" => pc.Offer(UpdatePc.Built, pc.Zip(UpdatePc.Built, archive => archive.CreateEntry("../outside.txt"))),
            "an older release relabelled" => pc.Offer(relabelled, pc.Zip(relabelled)),
            _ => offer,
        };
        pc.Folder().Write(pc.State.WithSequence(Manifest.Stable, 2));
        pc.Publish(
            wrong == "of the beta channel" ? served with { Channel = Manifest.Beta } : served,
            other: wrong == "signed by another key" ? other : null,
            at: Manifest.Stable);
        if (wrong == "an envelope too long")
        {
            pc.Channel.Serve("/v1/stable.json", new byte[ManifestEnvelope.MaxBytes + 1]);
        }

        if (wrong == "the release key revoked")
        {
            // The backup key's manifest, which revokes the release key; then the release key's offer, signed as before.
            using var revoking = pc.Updater();
            pc.Publish(pc.Offer(UpdatePc.Running, zip, sequence: 3) with { RevokedKey = pc.ReleaseKeyHash }, backup: true);
            await revoking.CheckAsync(TestContext.Current.CancellationToken);
            pc.Publish(offer with { Sequence = 4 });
        }

        return (pc.Build(catalog: wrong == "a release build without its catalog"), wrong != "a folder users write to");
    }

    private static Dictionary<string, Package> X64(Package package) => new(StringComparer.Ordinal) { ["x64"] = package };

    // Steps the clock once the updater waits on it, and waits for that tick's work; a build that never updates has no loop.
    private static async Task StepAsync(UpdatePc pc, Task running, TimeSpan step, bool minutes = false)
    {
        if (running.IsCompleted)
        {
            return;
        }

        await (minutes ? pc.Clock.PassAsync(step) : pc.Clock.StepAsync(step));
        await pc.Clock.SettledAsync();
    }

    private static string Asked(UpdatePc pc) => string.Join(' ', pc.Channel.Paths.Select(path =>
        path.StartsWith("/v1/zips/", StringComparison.Ordinal) ? "zip" : Path.GetFileNameWithoutExtension(path)));

    private static int Manifests(UpdatePc pc) => pc.Channel.Paths.Count(path => path.EndsWith(".json", StringComparison.Ordinal));
}
