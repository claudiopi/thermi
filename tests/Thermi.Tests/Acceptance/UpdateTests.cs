using System.Security.Cryptography;
using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// What the updates decide without Windows, spec 004: what a PC takes from a channel, when it may switch, whether a switch
/// worked, the owner's levers, and About's line.
/// </summary>
public sealed class UpdateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 21, 0, 0, TimeSpan.Zero);

    private static readonly Package Zip = new(new Uri("https://updates.thermi.app/v1/thermi-x.zip"), 35_000_000, new string('a', 64));

    private static ReleaseVersion V(string text) => ReleaseVersion.Parse(text) ?? throw new ArgumentException(text, nameof(text));

    private static ReleaseVersion? Maybe(string? text) => text is null ? null : V(text);

    // The state as "installed previous ready failed bucket sequence", "-" for none; the manifest as "channel sequence version
    // rollout", then its withdrawn versions and whether it is off. The answer is the plan's kind and version.
    [Theory]
    [InlineData("1.2.0 - - - 10 1", "stable 2 1.3.0 100", "", false, "Download 1.3.0")]
    [InlineData("1.3.0 1.2.0 - - 10 1", "stable 2 1.3.0 100", "", false, "Nothing")]
    [InlineData("1.3.0 1.2.0 - - 10 1", "stable 2 1.2.0 100", "", false, "Nothing")]
    [InlineData("1.2.0 - - 1.3.0 10 1", "stable 2 1.3.0 100", "", false, "Nothing")]
    [InlineData("1.2.0 - - - 50 1", "stable 2 1.3.0 50", "", false, "Nothing")]
    [InlineData("1.2.0 - - - 49 1", "stable 2 1.3.0 50", "", false, "Download 1.3.0")]
    [InlineData("1.2.0 - - - 49 1", "stable 3 1.3.0 80", "", false, "Download 1.3.0")]
    [InlineData("1.3.0 1.2.0 - - 10 1", "stable 2 1.2.0 100", "1.3.0", false, "RollBack 1.2.0")]
    [InlineData("1.3.0 - - - 10 1", "stable 2 1.2.0 100", "1.3.0", false, "Nothing")]
    [InlineData("1.3.0 1.2.0 1.4.0 - 10 1", "stable 2 1.2.0 100", "1.3.0", false, "RollBack 1.2.0")]
    [InlineData("1.2.0 - 1.4.0 - 10 1", "stable 2 1.2.0 100", "1.3.0", false, "DropReady")]
    [InlineData("1.2.0 - 1.3.0 - 10 1", "stable 2 1.3.0 100", "", false, "Keep 1.3.0")]
    [InlineData("1.2.0 - 1.3.0 - 10 1", "stable 2 1.3.0 100", "", true, "DropReady")]
    [InlineData("1.2.0 - 1.3.0 - 10 1", "stable 2 1.4.0 100", "", false, "Download 1.4.0")]
    [InlineData("1.2.0 - - - 10 5", "stable 4 1.3.0 100", "", false, "Refuse")]
    [InlineData("1.2.0 - - - 10 1", "beta 2 1.3.0 100", "", false, "Refuse")]
    [InlineData("1.2.0 - - - 10 1", "stable 2 1.3.0-beta.1 100", "", false, "Download 1.3.0-beta.1")]
    [InlineData("1.3.0 - - - 10 1", "stable 2 1.3.0-rc.2 100", "", false, "Nothing")]
    [InlineData("1.2.0 - - - 10 1", "stable 2 1.3.0 100 expired", "", false, "Refuse")]
    public void TheUpdaterNeverGoesBackButToItsOwnPrevious(string state, string manifest, string withdrawn, bool disabled, string plan)
    {
        var s = state.Split(' ');
        var m = manifest.Split(' ');
        var before = new UpdateState
        {
            Installed = Maybe(None(s[0])),
            Previous = Maybe(None(s[1])),
            Ready = Maybe(None(s[2])),
            Failed = None(s[3]) is { } failed ? [V(failed)] : [],
            Bucket = int.Parse(s[4], System.Globalization.CultureInfo.InvariantCulture),
        }.WithSequence(Manifest.Stable, long.Parse(s[5], System.Globalization.CultureInfo.InvariantCulture));
        var offered = new Manifest
        {
            Channel = m[0],
            Sequence = long.Parse(m[1], System.Globalization.CultureInfo.InvariantCulture),
            Version = V(m[2]),
            Packages = new Dictionary<string, Package>(StringComparer.Ordinal) { ["x64"] = Zip },
            Rollout = int.Parse(m[3], System.Globalization.CultureInfo.InvariantCulture),
            Withdrawn = [.. withdrawn.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(V)],
            Disabled = disabled,
            Expires = m.Length > 4 ? Now.AddMinutes(-1) : Now.AddDays(30),
        };

        var decided = UpdatePlan.Decide(before, new ManifestRead(offered, null), Manifest.Stable, "x64", Now);

        Assert.Equal(plan, decided.Version is { } version ? $"{decided.Kind} {version}" : decided.Kind.ToString());
    }

    // The window open, a drag, the minutes since the last input, the lock, every overlay hidden, and urgency.
    [Theory]
    [InlineData(false, false, 1, false, false, false, false)]
    [InlineData(false, false, 3, false, false, false, true)]
    [InlineData(false, false, 0, true, false, false, true)]
    [InlineData(false, false, 0, false, true, false, true)]
    [InlineData(true, false, 60, false, false, false, false)]
    [InlineData(false, true, 60, false, false, true, false)]
    [InlineData(false, false, 0, false, false, true, true)]
    [InlineData(true, false, 0, false, false, true, false)]
    public void AVersionSwitchesOnlyAtAQuietMoment(bool window, bool drag, int minutes, bool locked, bool hidden, bool urgent, bool quiet)
    {
        var session = new SessionFacts(window, drag, TimeSpan.FromMinutes(minutes), locked, hidden);

        Assert.Equal(quiet, UpdateSchedule.Quiet(session, urgent));
    }

    [Fact]
    public void AFailedSwitchGoesBackByWhatHappenedNotByTheClock()
    {
        // The watchdog's polls and the old overlay's ticks, which stop while Windows sleeps.
        Assert.Equal(Verdict.Waiting, SwitchHealth.Service(polls: 119, served: false));
        Assert.Equal(Verdict.Failed, SwitchHealth.Service(polls: 120, served: false));
        Assert.Equal(Verdict.Passed, SwitchHealth.Service(polls: 120, served: true));
        Assert.Equal(Verdict.Waiting, SwitchHealth.Overlay(ticks: 59, cameUp: false, exited: false));
        Assert.Equal(Verdict.Failed, SwitchHealth.Overlay(ticks: 3, cameUp: false, exited: true));
        Assert.Equal(Verdict.Failed, SwitchHealth.Overlay(ticks: 60, cameUp: false, exited: false));
        Assert.Equal(Verdict.Passed, SwitchHealth.Overlay(ticks: 10, cameUp: true, exited: false));

        // A switch the watchdog judged failed keeps the PC where it was, and the version is never taken again.
        var ready = new UpdateState { Installed = V("1.2.0"), Previous = V("1.1.0"), Ready = V("1.3.0") };
        var failed = SwitchHealth.Failed(SwitchHealth.Begin(ready, V("1.3.0")));
        Assert.Equal(("1.2.0", "1.3.0"), (failed.Installed!.ToString(), Assert.Single(failed.Failed).ToString()));
        Assert.Null(failed.Ready);

        // Once done, two runs of the new service that never stopped, within ten minutes, send the PC back.
        var done = SwitchHealth.Done(SwitchHealth.Begin(ready, V("1.3.0")), Now);
        var (first, step) = SwitchHealth.Started(done, V("1.3.0"), Now.AddSeconds(5));
        Assert.Equal(StartStep.Serve, step);
        var (second, again) = SwitchHealth.Started(first, V("1.3.0"), Now.AddMinutes(1));
        Assert.Equal(StartStep.Serve, again);
        Assert.Equal(StartStep.RollBack, SwitchHealth.Started(second, V("1.3.0"), Now.AddMinutes(2)).Step);

        // A run that ends past the window, as when the PC slept, fails nothing; a stop asked for is never a crash.
        Assert.Equal(StartStep.Serve, SwitchHealth.Started(second, V("1.3.0"), Now.AddHours(9)).Step);
        Assert.Equal(StartStep.Serve, SwitchHealth.Started(SwitchHealth.Stopped(second), V("1.3.0"), Now.AddMinutes(3)).Step);

        // The rollback itself withdraws nothing from the PC but the version that failed.
        var back = SwitchHealth.Done(SwitchHealth.Begin(second, V("1.2.0")), Now.AddMinutes(3));
        Assert.Equal(("1.2.0", "1.3.0"), (back.Installed!.ToString(), Assert.Single(back.Failed).ToString()));
    }

    [Fact]
    public void ASwitchCutBeforeItWasDoneIsTriedOnceMore()
    {
        var ready = new UpdateState { Installed = V("1.2.0"), Ready = V("1.3.0") };

        // Started as the new version, the switch was done but not written down; as the old one, it was cut.
        var finished = SwitchHealth.Started(SwitchHealth.Begin(ready, V("1.3.0")), V("1.3.0"), Now).State;
        Assert.Equal(("1.3.0", "1.2.0"), (finished.Installed!.ToString(), finished.Previous!.ToString()));
        var cut = SwitchHealth.Started(SwitchHealth.Begin(ready, V("1.3.0")), V("1.2.0"), Now).State;
        Assert.Equal(("1.2.0", "1.3.0"), (cut.Installed!.ToString(), cut.Ready!.ToString()));
        Assert.Empty(cut.Failed);
        var cutTwice = SwitchHealth.Started(SwitchHealth.Begin(cut, V("1.3.0")), V("1.2.0"), Now).State;
        Assert.Equal("1.3.0", Assert.Single(cutTwice.Failed).ToString());

        // A reboot with no switch under way changes nothing.
        Assert.Equal(cut, SwitchHealth.Started(cut, V("1.2.0"), Now).State);
    }
    [Fact]
    public void LeversNeverSendAPcBackward()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = key.ExportSubjectPublicKeyInfo();
        using var backupKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var backup = backupKey.ExportSubjectPublicKeyInfo();
        var zips = new Dictionary<string, Package>(StringComparer.Ordinal) { ["x64"] = Zip };
        var other = new Dictionary<string, Package>(StringComparer.Ordinal) { ["x64"] = Zip with { Sha256 = new string('b', 64) } };
        IReadOnlyDictionary<string, ChannelHistory> channels = new Dictionary<string, ChannelHistory>(StringComparer.Ordinal)
        {
            [Manifest.Stable] = new([]),
            [Manifest.Beta] = new([]),
        };

        // Stable's first version reaches beta too, which was behind.
        channels = Done(Levers.Publish(channels, Manifest.Stable, V("1.0.0"), zips, 100, urgent: false, Now));
        Assert.Equal("1.0.0 1.0.0", Offers(channels));
        channels = Done(Levers.Publish(channels, Manifest.Beta, V("1.1.0-beta.1"), zips, 100, urgent: false, Now));
        Assert.NotNull(Levers.Publish(channels, Manifest.Beta, V("1.0.5"), zips, 100, urgent: false, Now).Refused);
        Assert.NotNull(Levers.Publish(channels, Manifest.Beta, V("1.1.0-beta.1"), other, 100, urgent: false, Now).Refused);

        channels = Done(Levers.Promote(channels, Manifest.Beta, Manifest.Stable, rollout: 10, urgent: null, Now));
        Assert.Equal("1.1.0-beta.1 1.1.0-beta.1", Offers(channels));
        Assert.Equal(10, channels[Manifest.Stable].Current!.Rollout);

        // A rollback withdraws the version and offers the one before it; a channel off keeps its version.
        channels = Done(Levers.RollBack(channels, Manifest.Stable, Now));
        var rolledBack = channels[Manifest.Stable].Current!;
        Assert.Equal("1.0.0", rolledBack.Version!.ToString());
        Assert.Equal([V("1.1.0-beta.1")], rolledBack.Withdrawn);
        Assert.NotNull(Levers.RollBack(channels, Manifest.Stable, Now).Refused);
        channels = Done(Levers.Disable(channels, Manifest.Stable, Now));
        Assert.True(channels[Manifest.Stable].Current!.Disabled);
        channels = Done(Levers.Refresh(channels, Manifest.Stable, Now.AddDays(30)));
        Assert.Equal(Now.AddDays(75), channels[Manifest.Stable].Current!.Expires);

        // Every manifest a channel served took the next sequence, and opens with the key that sealed it.
        Assert.Equal([1L, 2, 3, 4, 5], channels[Manifest.Stable].Published.Select(manifest => manifest.Sequence));
        foreach (var manifest in channels.Values.SelectMany(history => history.Published))
        {
            Assert.Equal(manifest, ManifestEnvelope.Open(ManifestEnvelope.Seal(manifest, key), release, backup).Manifest, ManifestComparer.Instance);
        }

        // Only the backup key may revoke the release key, and once revoked only the backup key opens a manifest.
        var revoking = channels[Manifest.Stable].Current! with { Revokes = true };
        Assert.Equal(ManifestProblem.Invalid, ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, key), release, backup).Problem);
        Assert.True(ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, backupKey), release, backup).Backup);
        Assert.Equal(ManifestProblem.NotSigned, ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, key), null, backup).Problem);
    }

    // A build without a key, the policy, a version ready, the last successful check or the first, in days ago, and beta.
    [Theory]
    [InlineData(false, false, null, 1, null, false, UpdateLineKind.NotUpdating)]
    [InlineData(true, true, null, 1, null, false, UpdateLineKind.TurnedOff)]
    [InlineData(true, false, "1.3.0", 20, null, false, UpdateLineKind.Ready)]
    [InlineData(true, false, null, 13, null, true, UpdateLineKind.UpToDate)]
    [InlineData(true, false, null, 14, null, false, UpdateLineKind.Unreached)]
    [InlineData(true, false, null, null, 15, false, UpdateLineKind.Unreached)]
    [InlineData(true, false, null, null, 2, false, UpdateLineKind.NotChecked)]
    public void TheUpdateLineSaysWhatThisPcDoes(bool built, bool off, string? ready, int? success, int? first, bool beta, UpdateLineKind kind)
    {
        var policy = UpdatePolicy.Of(built, off ? 0 : null, beta ? " Beta " : "stable");
        var state = new UpdateState
        {
            Ready = Maybe(ready),
            LastSuccess = success is { } days ? Now.AddDays(-days) : null,
            FirstCheck = first is { } since ? Now.AddDays(-since) : null,
        };

        var line = UpdateStatus.Of(policy, state, Now);

        Assert.Equal(kind, line.Kind);
        Assert.Equal(beta, line.Beta);
    }

    private static string? None(string text) => text == "-" ? null : text;

    private static IReadOnlyDictionary<string, ChannelHistory> Done(LeverResult result)
    {
        Assert.Null(result.Refused);
        return result.Channels;
    }

    private static string Offers(IReadOnlyDictionary<string, ChannelHistory> channels) =>
        $"{channels[Manifest.Stable].Current?.Version} {channels[Manifest.Beta].Current?.Version}";

    // Records compare their collections by reference; a manifest read back holds new ones.
    private sealed class ManifestComparer : IEqualityComparer<Manifest?>
    {
        public static readonly ManifestComparer Instance = new();

        public bool Equals(Manifest? x, Manifest? y) =>
            x is not null && y is not null
            && (x with { Packages = y.Packages, Withdrawn = y.Withdrawn }) == y
            && x.Packages.OrderBy(p => p.Key, StringComparer.Ordinal).SequenceEqual(y.Packages.OrderBy(p => p.Key, StringComparer.Ordinal))
            && x.Withdrawn.SequenceEqual(y.Withdrawn);

        public int GetHashCode(Manifest? obj) => obj?.Sequence.GetHashCode() ?? 0;
    }
}
