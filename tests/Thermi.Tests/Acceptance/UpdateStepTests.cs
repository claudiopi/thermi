using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>Spec 004: what the service's updater decides as it carries a plan out, a download in and a switch through.</summary>
public sealed class UpdateStepTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly Package Zip = new(new Uri("https://updates.thermi.app/v1/thermi-x.zip"), 9, new string('a', 64));

    private static ReleaseVersion V(string text) => ReleaseVersion.Parse(text) ?? throw new ArgumentException(text, nameof(text));

    // The plan as "kind version", whether it is urgent, what became of its download and whether a switch is held; the state
    // left as "ready urgent refused" and the switch begun. The PC runs 1.2.0 with 1.3.0 ready and urgent, and a zip refused
    // before, "old". A zip refused for what it holds is never fetched again; a rollback goes back whatever holds, and one to
    // the version running is no step back.
    [Theory]
    [InlineData("Download 1.4.0", true, "Ready", false, "1.4.0 urgent - 1.4.0")]
    [InlineData("Download 1.4.0", false, "Ready", false, "1.4.0 - - -")]
    [InlineData("Download 1.4.0", true, "Ready", true, "1.4.0 urgent - -")]
    [InlineData("Download 1.4.0", true, "Refused", false, "1.3.0 urgent zip -")]
    [InlineData("Download 1.4.0", true, "Later", false, "1.3.0 urgent old -")]
    [InlineData("Keep 1.3.0", true, "Later", false, "1.3.0 urgent old 1.3.0")]
    [InlineData("Keep 1.3.0", false, "Later", false, "1.3.0 - old -")]
    [InlineData("DropReady", false, "Later", false, "- - old -")]
    [InlineData("RollBack 1.1.0", false, "Later", true, "1.3.0 urgent old 1.1.0")]
    [InlineData("RollBack 1.2.0", false, "Later", true, "1.3.0 urgent old -")]
    [InlineData("Nothing 1.4.0", false, "Ready", false, "1.3.0 urgent old -")]
    public void ACheckLeavesWhatItsPlanAndItsDownloadCameTo(string plan, bool urgent, string fetched, bool held, string left)
    {
        var parts = plan.Split(' ');
        var kind = Enum.Parse<PlanKind>(parts[0]);
        var planned = new Plan(kind, parts.Length > 1 ? V(parts[1]) : null, kind == PlanKind.Download ? Zip : null, urgent);
        var state = new UpdateState
        {
            Installed = V("1.2.0"),
            Previous = V("1.1.0"),
            Ready = V("1.3.0"),
            ReadyUrgent = true,
            Refused = "old",
            SwitchAfter = held ? Now.AddHours(1) : null,
        };

        var (next, to) = UpdatePlan.Apply(state, planned, Enum.Parse<Fetched>(fetched), V("1.2.0"), Now);

        var refused = next.Refused == Zip.Sha256 ? "zip" : next.Refused ?? "-";
        Assert.Equal(left, $"{next.Ready?.ToString() ?? "-"} {(next.ReadyUrgent ? "urgent" : "-")} {refused} {to?.ToString() ?? "-"}");
    }

    // A quiet session or About's restart switches the version installed, and only forward, to the version ready, once no hold
    // stops it.
    [Theory]
    [InlineData("1.2.0", "1.3.0", false, "1.3.0")]
    [InlineData("1.2.0", "1.3.0", true, null)]
    [InlineData("1.2.0", "1.2.0", false, null)]
    [InlineData("1.2.0", null, false, null)]
    [InlineData("1.1.0", "1.3.0", false, null)]
    public void AVersionReadySwitchesOnlyForwardFromTheOneInstalled(string installed, string? ready, bool held, string? switched)
    {
        var state = new UpdateState
        {
            Installed = V(installed),
            Ready = ready is null ? null : V(ready),
            SwitchAfter = held ? Now.AddHours(1) : null,
        };

        Assert.Equal(switched, SwitchHealth.ReadyFor(state, V("1.2.0"), Now)?.ToString());
    }

    // A file is the package only at its size and SHA-256; one longer is refused as such, as the download stops reading there.
    [Theory]
    [InlineData(10, 'a', "the file is longer than its 9 bytes")]
    [InlineData(8, 'b', "the file has 8 bytes, not 9")]
    [InlineData(9, 'b', "the file's SHA-256 is not the manifest's")]
    [InlineData(9, 'a', null)]
    public void AFileIsThePackageOnlyAtItsSizeAndHash(long bytes, char hash, string? refusal) =>
        Assert.Equal(refusal, Zip.Refusal(bytes, new string(hash, 64)));

    // A zip's entry lands inside the folder it is unpacked into, or the zip is refused: never a rooted path, even one inside
    // it, a drive's, a stream's, one that climbs out, even into a folder whose name starts as this one's, or a link.
    [Theory]
    [InlineData("thermi.dll", 0x81A4, true)]
    [InlineData("launchers/../thermi.exe", 0, true)]
    [InlineData("../outside.txt", 0, false)]
    [InlineData("../unpack-12/thermi.dll", 0, false)]
    [InlineData("FOLDER/thermi.dll", 0, false)]
    [InlineData("Z:thermi.dll", 0, false)]
    [InlineData("thermi.dll:hidden", 0, false)]
    [InlineData("launchers", 0xA1FF, false)]
    public void AZipsEntryLandsOnlyInsideItsFolder(string name, int mode, bool inside)
    {
        var folder = Path.Combine("update", "unpack-1");

        // FOLDER is the folder's own path from its drive's root, a rooted path with no drive to name.
        var full = Path.GetFullPath(folder);
        var entry = name.Replace("FOLDER", full[(Path.GetPathRoot(full)!.Length - 1)..], StringComparison.Ordinal);

        Assert.Equal(inside, ZipEntries.LandsInside(folder, entry, mode << 16));
    }
}
