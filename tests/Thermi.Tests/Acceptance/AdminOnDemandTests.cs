using System.Security.Cryptography;
using System.Xml.Linq;
using Thermi.Core.Install;
using Thermi.Core.Updates;

namespace Thermi.Tests.Acceptance;

/// <summary>Spec 004, R26 to R33: thermi without administrator rights, and the one prompt that adds them.</summary>
public sealed class AdminOnDemandTests
{
    private const string User = "S-1-5-21-1-2-3-1001";
    private const string Folder = @"users\ana\Programs\thermi";
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    // The service's pipe whenever it is installed; else the user's, trusted by its owner, its server's folder and integrity,
    // and the pipe's own label, read by an overlay at medium, high, or one whose own could not be read.
    [Theory] // Rows: each is one operand of Trusts, an owner of its pattern or an integrity at its edge; a security check holds each
    [InlineData("S-1-5-18", Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, 0x2000, 0x2000, true)]
    [InlineData("S-1-5-32-544", Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, 0x2000, 0x2000, true)]
    [InlineData("S-1-5-21-1-2-3-1002", Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, 0x2000, 0x2000, false)]
    [InlineData(User, @"users\ana\Downloads\thermi-sensors.exe", 0x2000, 0x2000, 0x2000, false)]
    [InlineData(User, Folder + @"\..\evil\thermi-sensors.exe", 0x2000, 0x2000, 0x2000, false)]
    [InlineData(User, Folder + @"\0.9.1\thermi-sensors.exe", 0x1000, 0x2000, 0x2000, false)]
    [InlineData(User, Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, 0x1000, 0x2000, false)]
    [InlineData(User, Folder + @"\0.9.1\thermi-sensors.exe", 0x3000, 0x2000, 0x3000, true)]
    [InlineData(User, Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, 0x2000, 0, false)]
    public void TheOverlayReadsTheServiceOrTheUsersOwnHost(string owner, string image, int integrity, int label, int own, bool trusted)
    {
        var server = new PipeServer(owner, image, integrity, label);

        Assert.Equal(("thermi-sensors", "thermi-sensors-" + User), (UserHost.Pipe(true, User), UserHost.Pipe(false, User)));
        Assert.Equal(trusted, UserHost.Trusts(server, User, Folder, own));
    }

    // Only an install under the user's own local app data is a per-user one, which updates itself and offers to install for
    // everyone while it reads the user's own host; a machine install whose service is missing serves the user's pipe and
    // does neither, and once the service is read the fix is the machine's again.
    [Theory] // Rows: each is one operand of InstallsForEveryone, or a folder outside local app data, a climb out of it among them
    [InlineData(@"users\ana\AppData\Local\Programs\thermi", false, true, true)]
    [InlineData(@"users\ana\AppData\Local\Programs\thermi", true, true, false)]
    [InlineData(@"Program Files\thermi", false, false, false)]
    [InlineData(@"users\ana\AppData\Local\..\Roaming\thermi", false, false, false)]
    public void APerUserInstallIsTheOneUnderTheUsersLocalAppData(string top, bool serviceRead, bool perUser, bool offered)
    {
        const string Local = @"users\ana\AppData\Local";

        Assert.Equal(perUser, UserHost.PerUserInstall(top, Local));
        Assert.Equal(offered, UserHost.InstallsForEveryone(top, Local, UserHost.Pipe(serviceRead, User)));
    }

    // The wait before the next host: 2 s, doubling to a minute after hosts that ended within 10 s, and none under the lock.
    [Theory] // Rows: each is one operand of NextTry; 11 s sits past the 10 s a quick end takes, and 40 doubles past the minute
    [InlineData(300, null, false, 2)]
    [InlineData(3, 2, false, 4)]
    [InlineData(3, 40, false, 60)]
    [InlineData(11, 40, false, 2)]
    [InlineData(3, null, true, null)]
    public void TheUsersHostIsStartedAgainAfterAWait(int ran, int? waited, bool locked, int? next) =>
        Assert.Equal(
            next is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            UserHost.NextTry(TimeSpan.FromSeconds(ran), waited is { } before ? TimeSpan.FromSeconds(before) : null, locked));

    // The MSI of the version the channel offers, the running one or later, listed for this processor; a channel off or the
    // running version withdrawn leaves none, and the fix opens the download page.
    [Theory] // Rows: each is one operand of Msi's condition; the running version is the edge of the >= that a later one passes
    [InlineData("1.3.0", false, false, "x64", true)]
    [InlineData("1.2.0", false, false, "x64", true)]
    [InlineData("1.1.0", false, false, "x64", false)]
    [InlineData(null, false, false, "x64", false)]
    [InlineData("1.3.0", true, false, "x64", false)]
    [InlineData("1.3.0", false, true, "x64", false)]
    [InlineData("1.3.0", false, false, "arm64", false)]
    public void TheMachineMsiIsTheOfferedVersionsForThisProcessor(string? offered, bool disabled, bool withdrawn, string processor, bool given)
    {
        var running = ReleaseVersion.Parse("1.2.0")!;
        var msi = new Package(new Uri("https://u/v1/thermi-1.3.0-x64.msi"), 40_000_000, new string('a', 64));
        var manifest = new Manifest
        {
            Channel = Manifest.Stable,
            Sequence = 1,
            Version = offered is null ? null : ReleaseVersion.Parse(offered),
            Disabled = disabled,
            Withdrawn = withdrawn ? [running] : [],
            Msis = new Dictionary<string, Package> { ["x64"] = msi },
        };

        Assert.Equal(given ? msi : null, InstallForEveryone.Msi(manifest, running, processor));
    }

    // Before any MSI runs: the download page with checks turned off by the policy or no MSI offered, and a failure when no
    // manifest could be taken; the offered MSI, running, when there is one.
    [Theory] // Rows: each is one operand of MsiToRun, an outcome A38 states
    [InlineData(false, "1.3.0", MachineInstallOutcome.DownloadPage)]
    [InlineData(true, null, MachineInstallOutcome.Failed)]
    [InlineData(true, "1.1.0", MachineInstallOutcome.DownloadPage)]
    [InlineData(true, "1.3.0", MachineInstallOutcome.Running)]
    public void InstallForEveryoneRunsAnMsiOnlyWhenTheChannelOffersOne(bool checks, string? offered, MachineInstallOutcome outcome)
    {
        var msis = new Dictionary<string, Package> { ["x64"] = new(new Uri("https://u/v1/thermi.msi"), 40_000_000, new string('a', 64)) };
        var manifest = offered is null ? null : new Manifest { Channel = Manifest.Stable, Sequence = 1, Version = ReleaseVersion.Parse(offered), Msis = msis };

        Assert.Equal(
            (outcome, outcome == MachineInstallOutcome.Running ? msis["x64"] : null),
            InstallForEveryone.MsiToRun(checks, manifest, ReleaseVersion.Parse("1.2.0")!, "x64"));
    }

    // The pinned driver or a later one is never installed over; an older one, or one whose version cannot be read, is. A
    // version of two parts reads as older than the same of three, and is installed over, which does no harm.
    [Theory] // Rows: each is one side of the >= on the pinned version, or a version read in two parts or not at all
    [InlineData("2.2.0", true)]
    [InlineData("2.3", true)]
    [InlineData("2.1.9", false)]
    [InlineData("2.2", false)]
    [InlineData("two", false)]
    public void OnlyAnOlderDriverIsInstalledOver(string installed, bool covered) =>
        Assert.Equal(covered, DriverVersion.Covers(new Version(2, 2, 0), installed));

    // A version's machine MSIs travel with it through every lever and its signature, and a manifest from before them reads.
    [Fact]
    public void TheManifestListsEachVersionsMachineMsi()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = key.ExportSubjectPublicKeyInfo();
        var msi = new Package(new Uri("https://u/v1/msis/1.1.0/thermi-1.1.0-x64.msi"), 40_000_000, new string('c', 64));
        var zips = new Dictionary<string, Package> { ["x64"] = new(new Uri("https://u/v1/z.zip"), 9, new string('a', 64)) };
        var files = new VersionFiles(zips, new Dictionary<string, Package> { ["x64"] = msi });
        var empty = new Dictionary<string, ChannelHistory> { [Manifest.Stable] = new([]), [Manifest.Beta] = new([]) };

        var none = new Dictionary<string, Package>();
        var channels = Levers.Publish(empty, Manifest.Stable, ReleaseVersion.Parse("1.0.0")!, files with { Msis = none }, 100, false, Now).Channels;
        channels = Levers.Publish(channels, Manifest.Beta, ReleaseVersion.Parse("1.1.0")!, files, 100, false, Now).Channels;
        var refused = Levers.Publish(channels, Manifest.Beta, ReleaseVersion.Parse("1.1.0")!, files with { Msis = zips }, 100, false, Now).Refused;
        channels = Levers.Promote(channels, Manifest.Beta, Manifest.Stable, 100, null, Now).Channels;
        var promoted = channels[Manifest.Stable].Current!;
        var rolledBack = Levers.RollBack(channels, Manifest.Stable, Now).Channels[Manifest.Stable].Current!;
        var opened = ManifestEnvelope.Open(ManifestEnvelope.Seal(promoted, key), release, release).Manifest;
        var overHttp = promoted with { Msis = new Dictionary<string, Package> { ["x64"] = msi with { Url = new Uri("http://u/m.msi") } } };
        var unsafeOne = ManifestEnvelope.Open(ManifestEnvelope.Seal(overHttp, key), release, release);
        var before = ManifestEnvelope.Open(ManifestEnvelope.Seal(rolledBack, key), release, release).Manifest;

        Assert.NotNull(refused);
        Assert.Equal(msi, promoted.Msis["x64"]);
        Assert.Equal(promoted, opened, UpdateTests.ManifestComparer.Instance);
        Assert.Equal(ManifestProblem.Invalid, unsafeOne.Problem);
        Assert.Empty(before!.Msis);
    }

    // What msiexec's exit says of "install for everyone": installed, with or without a restart for the driver; refused at
    // the prompt or in Windows Installer's own window; or failed.
    [Theory] // Rows: each exit is a constant of the switch, which Stryker does not mutate
    [InlineData(0, MachineInstallOutcome.Installed)]
    [InlineData(3010, MachineInstallOutcome.Installed)]
    [InlineData(1223, MachineInstallOutcome.Refused)]
    [InlineData(1602, MachineInstallOutcome.Refused)]
    [InlineData(1603, MachineInstallOutcome.Failed)]
    public void MsiexecsExitIsWhatTheInstallCameTo(int exit, MachineInstallOutcome outcome) =>
        Assert.Equal(outcome, InstallForEveryone.OutcomeOf(exit));

    // The machine MSI's driver property reaches the elevated part, as a sensors-only install's driver comes during setup,
    // and its action never runs on a repair.
    [Fact]
    public void TheDriverPropertyActsOnlyOnAFirstInstall()
    {
        var wix = XDocument.Load(Path.Combine(Root(), "src", "Thermi.Installer", "Package.wxs"));
        var all = wix.Descendants().ToList();

        var property = all.Single(element => element.Name.LocalName == "Property" && (string?)element.Attribute("Id") == InstallForEveryone.DriverProperty);
        var action = all.Single(element => element.Name.LocalName == "Custom" && (string?)element.Attribute("Action") == "InstallDriver");

        Assert.Equal(
            ("yes", "NOT Installed AND (ADDDRIVER = 1 OR SENSORSONLY = 1)"), ((string?)property.Attribute("Secure"), (string?)action.Attribute("Condition")));
    }

    // Every program the MSIs run goes through WiX's own DLL actions, native to the package's processor, each given its
    // command line by a property set before it, so no console window shows as SYSTEM for a user to drive; the overlay
    // is asked of Explorer, which starts it as the person, unelevated.
    [Fact]
    public void TheMsiRunsNoProgramInAWindowAndAsksExplorerForTheOverlay()
    {
        var wix = XDocument.Load(Path.Combine(Root(), "src", "Thermi.Installer", "Package.wxs"));
        var all = wix.Descendants().ToList();
        var set = all.Where(element => element.Name.LocalName == "SetProperty" && (string?)element.Attribute("Sequence") == "execute").ToList();

        // Each action's entry in the util extension's DLL, or "not quiet" for one that runs its program any other way, and
        // the property set just before it.
        var actions = all.Where(element => element.Name.LocalName == "CustomAction").Select(action =>
            ((string?)action.Attribute("Id"), (string?)action.Attribute("BinaryRef") == "$(var.UtilCA)" && action.Attribute("ExeCommand") is null
                ? (string?)action.Attribute("DllEntry") : "not quiet",
                set.SingleOrDefault(each => (string?)each.Attribute("Before") == (string?)action.Attribute("Id"))?.Attribute("Id")?.Value));
        var binary = wix.DescendantNodes().OfType<XProcessingInstruction>()
            .Single(each => each.Target == "define" && each.Data.StartsWith("UtilCA", StringComparison.Ordinal));
        var overlay = all.Single(element => element.Name.LocalName == "CustomAction" && (string?)element.Attribute("Id") == "StartOverlay");
        var start = all.Single(element => element.Name.LocalName == "Custom" && (string?)element.Attribute("Action") == "StartOverlay");

        Assert.Equal(
            [("Adopt", "WixQuietExec", "Adopt"), ("Undo", "WixQuietExec", "Undo"), ("Forget", "WixQuietExec", "Forget"),
                ("TakeOff", "WixQuietExec", "TakeOff"), ("InstallDriver", "WixQuietExec", "InstallDriver"),
                ("StartOverlay", "WixUnelevatedShellExec", "WixUnelevatedShellExecTarget")],
            actions);
        Assert.Equal(
            ("UtilCA = \"Wix4UtilCA_$(sys.BUILDARCHSHORT)\"", "immediate", "yes", "NOT REMOVE AND UILevel > 3 AND NOT SENSORSONLY = 1"),
            (binary.Data.Trim(), (string?)overlay.Attribute("Execute"), (string?)overlay.Attribute("Impersonate"), (string?)start.Attribute("Condition")));
    }

    // Spec 003, R24: either MSI puts its folder, where thermi-cli is, on PATH, the machine's or the user's own; per machine,
    // SENSORSONLY=1, or the choice an upgrade finds written, leaves out the overlay's feature, with its Start menu entry
    // and its start at sign-in, and writes the value the agents read; features are never migrated, so SENSORSONLY=0
    // brings the overlay back.
    [Fact]
    public void TheMsiPutsThermiCliOnPathAndMayLeaveTheOverlayOut()
    {
        var wix = XDocument.Load(Path.Combine(Root(), "src", "Thermi.Installer", "Package.wxs"));
        var all = wix.Descendants().ToList();
        XElement Single(string name, string attribute, string value)
        {
            return all.Single(element => element.Name.LocalName == name && (string?)element.Attribute(attribute) == value);
        }

        var path = Single("Environment", "Id", "Path");
        var scopes = wix.DescendantNodes().OfType<XProcessingInstruction>()
            .Where(each => each.Target == "define" && each.Data.StartsWith("SystemPath", StringComparison.Ordinal)).Select(each => each.Data.Trim());
        var overlay = Single("Feature", "Id", "Overlay");
        var choice = Single("RegistryValue", "Name", "SensorsOnly");
        var kept = Single("SetProperty", "Id", "SENSORSONLY");

        Assert.Equal(
            ("PATH", "[installfolder]", "last", "$(var.SystemPath)"),
            ((string?)path.Attribute("Name"), (string?)path.Attribute("Value"), (string?)path.Attribute("Part"), (string?)path.Attribute("System")));
        Assert.Equal(["SystemPath = \"no\"", "SystemPath = \"yes\""], scopes);
        Assert.Equal(2, all.Count(element => element.Name.LocalName == "ComponentRef" && (string?)element.Attribute("Id") == "Path"));
        Assert.Equal(
            ("SENSORSONLY = 1", "StartMenu ActiveSetup"),
            ((string?)overlay.Elements().Single(element => element.Name.LocalName == "Level").Attribute("Condition"),
                string.Join(" ", overlay.Elements().Where(element => element.Name.LocalName == "ComponentRef")
                    .Select(element => (string?)element.Attribute("Id")))));
        Assert.Equal(
            ("HKLM", @"SOFTWARE\thermi", "[SensorsChoice]", "NOT SENSORSONLY AND SENSORSONLYBEFORE = \"#1\"", "no"),
            ((string?)choice.Attribute("Root"), (string?)choice.Attribute("Key"), (string?)choice.Attribute("Value"), (string?)kept.Attribute("Condition"),
                (string?)all.Single(element => element.Name.LocalName == "MajorUpgrade").Attribute("MigrateFeatures")));
    }

    // The machine's overlay finds the per-user product to remove by the UpgradeCode the per-user MSI is built with.
    [Fact]
    public void ThePerUserMsiCarriesTheUpgradeCodeItIsRemovedBy()
    {
        var wix = XDocument.Load(Path.Combine(Root(), "src", "Thermi.Installer", "Package.wxs"));

        var code = wix.DescendantNodes().OfType<XProcessingInstruction>()
            .SkipWhile(each => each.Target != "if" || !each.Data.Contains("\"perUser\"", StringComparison.Ordinal))
            .First(each => each.Target == "define" && each.Data.StartsWith("UpgradeCode", StringComparison.Ordinal))
            .Data.Split('"')[1];

        Assert.Equal(InstallForEveryone.PerUserUpgradeCode, code);
    }

    private static string Root()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "thermi.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("thermi.slnx not found");
    }
}
