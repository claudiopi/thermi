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
    [Theory]
    [InlineData(User, Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, 0x2000, 0x2000, true)]
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
    // everyone; a machine install whose service is missing serves the user's pipe and does neither.
    [Theory]
    [InlineData(@"users\ana\AppData\Local\Programs\thermi", true)]
    [InlineData(@"Program Files\thermi", false)]
    [InlineData(@"users\ana\AppData\Local\..\Roaming\thermi", false)]
    public void APerUserInstallIsTheOneUnderTheUsersLocalAppData(string top, bool perUser) =>
        Assert.Equal(perUser, UserHost.PerUserInstall(top, @"users\ana\AppData\Local"));

    // The wait before the next host: 2 s, doubling to a minute after hosts that ended within 10 s, and none under the lock.
    [Theory]
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
    [Theory]
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
    [Theory]
    [InlineData(0, MachineInstallOutcome.Installed)]
    [InlineData(3010, MachineInstallOutcome.Installed)]
    [InlineData(1223, MachineInstallOutcome.Refused)]
    [InlineData(1602, MachineInstallOutcome.Refused)]
    [InlineData(1603, MachineInstallOutcome.Failed)]
    public void MsiexecsExitIsWhatTheInstallCameTo(int exit, MachineInstallOutcome outcome) =>
        Assert.Equal(outcome, InstallForEveryone.OutcomeOf(exit));

    // The machine MSI's driver property reaches the elevated part, and its action never runs on a repair.
    [Fact]
    public void TheDriverPropertyActsOnlyOnAFirstInstall()
    {
        var wix = XDocument.Load(Path.Combine(Root(), "src", "Thermi.Installer", "Package.wxs"));
        var all = wix.Descendants().ToList();

        var property = all.Single(element => element.Name.LocalName == "Property" && (string?)element.Attribute("Id") == InstallForEveryone.DriverProperty);
        var action = all.Single(element => element.Name.LocalName == "Custom" && (string?)element.Attribute("Action") == "InstallDriver");

        Assert.Equal(("yes", "NOT Installed AND ADDDRIVER = 1"), ((string?)property.Attribute("Secure"), (string?)action.Attribute("Condition")));
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
