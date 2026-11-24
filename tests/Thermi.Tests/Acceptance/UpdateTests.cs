using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Thermi.Core.Translations;
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

    private static Dictionary<string, Package> Zips(string processor, Package zip) => new(StringComparer.Ordinal) { [processor] = zip };

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
    [InlineData("1.2.0 - - - 10 1", "stable 2 1.3.0 100 far", "", false, "Refuse")]
    [InlineData("1.2.0 - - - 10 1", "stable 2 1.3.0 100 near", "", false, "Download 1.3.0")]
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
            Expires = m.Length < 5 ? Now.AddDays(30) : m[4] switch
            {
                "far" => Now + Manifest.Lifetime + TimeSpan.FromDays(2),
                "near" => Now + Manifest.Lifetime + TimeSpan.FromHours(12),
                _ => Now.AddMinutes(-1),
            },
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

        // Once done, the new service runs; two of its runs that end without stopping, within ten minutes, send the PC back.
        var done = SwitchHealth.Done(SwitchHealth.Begin(ready, V("1.3.0")), Now);
        var (second, step) = SwitchHealth.Started(done, V("1.3.0"), Now.AddSeconds(5));
        Assert.Equal(StartStep.Serve, step);
        Assert.Equal(StartStep.RollBack, SwitchHealth.Started(second, V("1.3.0"), Now.AddMinutes(1)).Step);
        Assert.Equal(StartStep.Serve, SwitchHealth.Started(second with { Failed = [V("1.2.0")] }, V("1.3.0"), Now.AddMinutes(1)).Step);

        // A run that ends past the window, as when the PC slept, fails nothing; a stop asked for is never a crash.
        Assert.Equal(StartStep.Serve, SwitchHealth.Started(second, V("1.3.0"), Now.AddHours(9)).Step);
        Assert.Equal(StartStep.Serve, SwitchHealth.Started(SwitchHealth.Stopped(second), V("1.3.0"), Now.AddMinutes(3)).Step);

        // The rollback itself withdraws nothing from the PC but the version that failed.
        var back = SwitchHealth.Done(SwitchHealth.Begin(second, V("1.2.0"), back: true), Now.AddMinutes(3));
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

        // A rollback cut twice is dropped, and says nothing against the version it went back to.
        var running = new UpdateState { Installed = V("1.3.0"), Previous = V("1.2.0") };
        var backCut = SwitchHealth.Started(SwitchHealth.Begin(running, V("1.2.0"), back: true), V("1.3.0"), Now).State;
        var backCutTwice = SwitchHealth.Started(SwitchHealth.Begin(backCut, V("1.2.0"), back: true), V("1.3.0"), Now).State;
        Assert.Empty(backCutTwice.Failed);
        Assert.Null(backCutTwice.Switch);

        // A version that came urgent stays urgent for the overlays still on the one before.
        Assert.True(SwitchHealth.Done(SwitchHealth.Begin(ready with { ReadyUrgent = true }, V("1.3.0")), Now).InstalledUrgent);
        Assert.False(finished.InstalledUrgent);
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

        // Nothing to promote or take back yet, and a rollout is a percentage.
        Assert.NotNull(Levers.Promote(channels, Manifest.Beta, Manifest.Stable, rollout: 100, urgent: null, Now).Refused);
        Assert.NotNull(Levers.RollBack(channels, Manifest.Stable, Now).Refused);
        Assert.All((int[])[-1, 101], rollout => Assert.NotNull(Levers.Publish(channels, Manifest.Stable, V("1.0.0"), zips, rollout, false, Now).Refused));

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

        // Only the backup key revokes the release key, which it names, in every manifest after until cleared; then only the backup key opens one.
        var releaseHash = Convert.ToHexStringLower(SHA256.HashData(release));
        Assert.NotNull(Levers.Revoke(channels, Manifest.Stable, "not a hash", Now).Refused);
        Assert.NotNull(Levers.Revoke(channels, Manifest.Stable, releaseHash.ToUpperInvariant(), Now).Refused);
        var revoked = Done(Levers.Revoke(channels, Manifest.Stable, releaseHash, Now));
        var revoking = revoked[Manifest.Stable].Current!;
        Assert.Equal(releaseHash, Done(Levers.Refresh(revoked, Manifest.Stable, Now))[Manifest.Stable].Current!.RevokedKey);
        Assert.Null(Done(Levers.Unrevoke(revoked, Manifest.Stable, Now))[Manifest.Stable].Current!.RevokedKey);
        Assert.Equal(ManifestProblem.Invalid, ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, key), release, backup).Problem);
        Assert.True(ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, backupKey), release, backup).Backup);
        Assert.Equal(ManifestProblem.NotSigned, ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, key), null, backup).Problem);

        // It sets the sequence back for the key it names, once: a replayed one, or one naming another key, does not.
        var revocation = ManifestEnvelope.Open(ManifestEnvelope.Seal(revoking, backupKey), release, backup);
        var ahead = new UpdateState { Installed = V("1.2.0") }.WithSequence(Manifest.Stable, 99);
        Assert.NotEqual(PlanKind.Refuse, UpdatePlan.Decide(ahead, revocation, Manifest.Stable, "x64", Now, releaseHash).Kind);
        Assert.Equal(PlanKind.Refuse, UpdatePlan.Decide(ahead, revocation, Manifest.Stable, "x64", Now, new string('0', 64)).Kind);
        Assert.Equal(PlanKind.Refuse, UpdatePlan.Decide(ahead with { RevokedKey = releaseHash }, revocation, Manifest.Stable, "x64", Now, releaseHash).Kind);

        // What a server or a cache could serve instead of an envelope, and a history read back as the levers wrote it.
        var sealedOne = ManifestEnvelope.Seal(rolledBack, key);
        Assert.Equal(ManifestProblem.TooLong, ManifestEnvelope.Open(new byte[ManifestEnvelope.MaxBytes + 1], release, backup).Problem);
        Assert.All(
            (string[])["", "{", "[]", """{ "manifest": "not base64", "signature": "" }"""],
            served => Assert.Equal(ManifestProblem.Malformed, ManifestEnvelope.Open(Encoding.UTF8.GetBytes(served), release, backup).Problem));
        var signed = Convert.ToBase64String(key.SignData("[1]"u8, HashAlgorithmName.SHA256));
        var notAManifest = Encoding.UTF8.GetBytes($$"""{ "manifest": "{{Convert.ToBase64String("[1]"u8)}}", "signature": "{{signed}}" }""");
        Assert.Equal(ManifestProblem.Invalid, ManifestEnvelope.Open(notAManifest, release, backup).Problem);
        Assert.Equal(ManifestProblem.NotSigned, ManifestEnvelope.Open(sealedOne, [1, 2, 3], [4, 5, 6]).Problem);
        var history = channels[Manifest.Stable];
        Assert.Equal(history.Published, ChannelHistory.Open(history.Seal(backupKey), release, backup)!.Published, ManifestComparer.Instance);
        Assert.Empty(ChannelHistory.Open(null, release, backup)!.Published);
        Assert.All([history.Seal(key), "{"u8.ToArray()], planted => Assert.Null(ChannelHistory.Open(planted, backup, backup)));

        // A history keeps its last manifests and a manifest its last withdrawn versions, so neither grows without end.
        for (var refresh = 0; refresh < ChannelHistory.Kept; refresh++)
        {
            channels = Done(Levers.Refresh(channels, Manifest.Stable, Now));
        }

        Assert.Equal(ChannelHistory.Kept, channels[Manifest.Stable].Published.Count);
        var crowded = channels[Manifest.Stable].Current! with
        {
            Version = V("2.0.0"),
            Withdrawn = [.. Enumerable.Range(1, Levers.WithdrawnKept).Select(n => V($"1.9.{n}"))],
        };
        IReadOnlyDictionary<string, ChannelHistory> full = new Dictionary<string, ChannelHistory>(channels, StringComparer.Ordinal)
        {
            [Manifest.Stable] = new([.. channels[Manifest.Stable].Published, crowded]),
        };
        var withdrawn = Done(Levers.RollBack(full, Manifest.Stable, Now))[Manifest.Stable].Current!.Withdrawn;
        Assert.Equal(Levers.WithdrawnKept, withdrawn.Count);
        Assert.Equal(V("2.0.0"), withdrawn[^1]);
    }

    // Signed is not enough: a manifest the release key signed by mistake, or of a newer schema, is refused whole.
    [Fact]
    public void ASignedManifestIsStillCheckedWhole()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = key.ExportSubjectPublicKeyInfo();
        var good = new Manifest { Channel = Manifest.Stable, Sequence = 3, Version = V("1.2.0"), Packages = Zips("x64", Zip) };
        Manifest[] refused =
        [
            good with { Schema = 2 }, good with { Channel = "nightly" }, good with { Sequence = 0 }, good with { Rollout = 101 },
            good with { Rollout = -1 }, good with { Packages = Zips("x86", Zip) },
            good with { Packages = Zips("x64", Zip with { Url = new Uri("http://updates.thermi.app/z.zip") }) },
            good with { Packages = Zips("x64", Zip with { Url = new Uri("ftp://127.0.0.1/z.zip") }) },
            good with { Packages = Zips("x64", Zip with { Size = 0 }) },
            good with { Packages = Zips("x64", Zip with { Size = ManifestEnvelope.MaxPackage + 1 }) },
            good with { Packages = Zips("x64", Zip with { Sha256 = new string('A', 64) }) },
            good with { Packages = Zips("x64", Zip with { Sha256 = "ab" }) },
        ];

        Assert.All(
            refused,
            manifest => Assert.Equal(ManifestProblem.Invalid, ManifestEnvelope.Open(ManifestEnvelope.Seal(manifest, key), release, release).Problem));
        var local = good with { Packages = Zips("arm64", Zip with { Url = new Uri("http://127.0.0.1:8765/v1/z.zip") }) };
        Assert.Null(ManifestEnvelope.Open(ManifestEnvelope.Seal(local, key), release, release).Problem);
    }

    // A version put in place by hand, as the MSI does, is what the PC runs from then on, with nothing to go back to.
    [Fact]
    public void AVersionInstalledByHandIsAdopted()
    {
        var state = new UpdateState { Installed = V("1.2.0"), Previous = V("1.1.0"), Ready = V("1.3.0"), Probation = new Probation(V("1.1.0"), Now, 1, false) };

        var (adopted, step) = SwitchHealth.Started(state, V("1.3.0"), Now);

        Assert.Equal(StartStep.Serve, step);
        Assert.Equal(V("1.3.0"), adopted.Installed);
        Assert.Null(adopted.Ready);
        Assert.Null(adopted.Probation);
        Assert.Equal(V("1.3.1"), SwitchHealth.Started(state with { Ready = V("1.3.1") }, V("1.3.0"), Now).State.Ready);
    }

    // A resume checks again only once the last check is as old as the schedule's; a metered network downloads only what is urgent.
    [Fact]
    public void AResumeAndAMeteredNetworkWaitForTheSchedule()
    {
        Assert.True(UpdateSchedule.DueOnResume(Now, null) && UpdateSchedule.DueOnResume(Now, Now - UpdateSchedule.Every));
        Assert.False(UpdateSchedule.DueOnResume(Now, Now.AddHours(-1)));
        (bool Metered, bool Urgent)[] networks = [(false, false), (false, true), (true, false), (true, true)];
        Assert.Equal([true, true, false, true], networks.Select(each => UpdateSchedule.MayDownload(each.Metered, each.Urgent)));
    }

    // A version names a folder, so it is read strictly, and ordered as SemVer orders them.
    [Fact]
    public void AVersionIsReadStrictly()
    {
        Assert.All(
            (string?[])[null, "", "1.2", "1.2.3.4", "01.2.3", "1.2.x", "1.2.3-alpha.1", "1.2.3-rc.0", "1.2.3-beta", @"..\1.2.3", new string('1', 41)],
            text => Assert.Null(ReleaseVersion.Parse(text)));
        Assert.True(V("1.2.3-beta.2") < V("1.2.3-rc.1") && V("1.2.3-rc.1") < V("1.2.3") && V("1.2.3") < V("1.10.0"));
        Assert.True(V("1.0.0") > null && V("1.0.0") >= V("1.0.0") && V("1.0.0") <= V("1.0.0"));
        Assert.Equal("1.2.3-rc.4", V("1.2.3-rc.4").ToString());
    }

    // A state file broken or missing is a PC that knows nothing yet.
    [Fact]
    public void TheStateFileReadsBackOrStartsOver()
    {
        var state = new UpdateState { Installed = V("1.2.0"), Previous = V("1.1.0"), Failed = [V("1.3.0")] }.WithSequence(Manifest.Beta, 7);
        var read = UpdateState.Read(state.Write());
        Assert.Equal((state.Installed, state.Previous), (read.Installed, read.Previous));
        Assert.Equal((7L, 0L), (read.SequenceOf(Manifest.Beta), read.SequenceOf(Manifest.Stable)));
        Assert.Equal(state.Failed, read.Failed);
        Assert.All((string?[])[null, "", "{", "null", """{ "installed": "../x" }"""], text => Assert.Null(UpdateState.Read(text).Installed));
    }

    // A build's address and signers (a release one without them never updates), the policy, a ready version, the last checks, beta.
    [Theory]
    [InlineData(null, false, null, 1, null, false, UpdateLineKind.NotUpdating)]
    [InlineData("https://u/v1/", false, null, 1, null, false, UpdateLineKind.NotUpdating)]
    [InlineData("http://127.0.0.1:8765/v1/", false, null, 1, null, false, UpdateLineKind.UpToDate)]
    [InlineData("https://u/v1/ AB", true, null, 1, null, false, UpdateLineKind.TurnedOff)]
    [InlineData("https://u/v1/ AB", false, "1.3.0", 20, null, false, UpdateLineKind.Ready)]
    [InlineData("https://u/v1/ AB", false, null, 13, null, true, UpdateLineKind.UpToDate)]
    [InlineData("https://u/v1/ AB", false, null, 14, null, false, UpdateLineKind.Unreached)]
    [InlineData("https://u/v1/ AB", false, null, null, 15, false, UpdateLineKind.Unreached)]
    [InlineData("https://u/v1/ AB", false, null, null, 2, false, UpdateLineKind.NotChecked)]
    public void TheUpdateLineSaysWhatThisPcDoes(string? build, bool off, string? ready, int? success, int? first, bool beta, UpdateLineKind kind)
    {
        var parts = build?.Split(' ');
        var policy = UpdatePolicy.Of(UpdatePolicy.Updates(parts?[0], parts?.ElementAtOrDefault(1)), off ? 0 : null, beta ? " Beta " : "stable");
        var state = new UpdateState
        {
            Ready = Maybe(ready),
            LastSuccess = success is { } days ? Now.AddDays(-days) : null,
            FirstCheck = first is { } since ? Now.AddDays(-since) : null,
        };

        var line = UpdateStatus.Of(policy, state, Now);

        Assert.Equal(kind, line.Kind);
        Assert.Equal(policy.Built && !off, policy.Checks);
        Assert.Equal(beta, line.Beta);
        Assert.All(Texts.Languages, language =>
        {
            var said = UpdateStatus.Say(line, Texts.Of(language), CultureInfo.GetCultureInfo(language), TimeZoneInfo.Utc);
            Assert.Equal(ready is not null, said.Contains(ready ?? "-", StringComparison.Ordinal));
        });
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
