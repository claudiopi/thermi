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

    // The service's pipe whenever it is installed; else the user's, trusted by its owner, its server's folder and integrity;
    // and the wait before the next host, 2 s, doubling to a minute after hosts that ended within 10 s, none under the lock.
    [Theory]
    [InlineData(true, "S-1-5-18", Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, true)]
    [InlineData(false, User, Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, true)]
    [InlineData(false, "S-1-5-32-544", Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, true)]
    [InlineData(false, "S-1-5-21-1-2-3-1002", Folder + @"\0.9.1\thermi-sensors.exe", 0x2000, false)]
    [InlineData(false, User, @"users\ana\Downloads\thermi-sensors.exe", 0x2000, false)]
    [InlineData(false, User, Folder + @"\..\evil\thermi-sensors.exe", 0x2000, false)]
    [InlineData(false, User, Folder + @"\0.9.1\thermi-sensors.exe", 0x1000, false)]
    public void TheOverlayReadsTheServiceOrTheUsersOwnHost(bool service, string owner, string image, int integrity, bool trusted)
    {
        var pipe = UserHost.Pipe(service, User);
        var server = new PipeServer(owner, image, integrity);

        Assert.Equal(service ? "thermi-sensors" : "thermi-sensors-" + User, pipe);
        Assert.Equal(trusted, service || UserHost.Trusts(server, User, Folder, ownIntegrity: 0x2000));
        Assert.Equal(
            (TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(2), null),
            (UserHost.NextTry(ran: TimeSpan.FromMinutes(5), waited: null, locked: false),
                UserHost.NextTry(ran: TimeSpan.FromSeconds(3), waited: TimeSpan.FromSeconds(2), locked: false),
                UserHost.NextTry(ran: TimeSpan.FromSeconds(3), waited: TimeSpan.FromSeconds(40), locked: false),
                UserHost.NextTry(ran: TimeSpan.FromSeconds(11), waited: TimeSpan.FromSeconds(40), locked: false),
                UserHost.NextTry(ran: TimeSpan.FromSeconds(3), waited: null, locked: true)));
    }

    // Only a per-user install offers the machine one, and only the MSI of the version the channel offers, the running one
    // or later, listed for this processor; a channel off, a version withdrawn or the policy's no opens the download page.
    [Theory]
    [InlineData(false, false, "1.3.0", false, false, "x64", FixOffer.None)]
    [InlineData(true, false, "1.3.0", false, false, "x64", FixOffer.MachineMsi)]
    [InlineData(true, false, "1.2.0", false, false, "x64", FixOffer.MachineMsi)]
    [InlineData(true, false, "1.1.0", false, false, "x64", FixOffer.DownloadPage)]
    [InlineData(true, false, null, false, false, "x64", FixOffer.DownloadPage)]
    [InlineData(true, true, "1.3.0", false, false, "x64", FixOffer.DownloadPage)]
    [InlineData(true, false, "1.3.0", true, false, "x64", FixOffer.DownloadPage)]
    [InlineData(true, false, "1.3.0", false, true, "x64", FixOffer.DownloadPage)]
    [InlineData(true, false, "1.3.0", false, false, "arm64", FixOffer.DownloadPage)]
    public void TheFixOffersTheMachineInstallOnlyFromAPerUserOne(
        bool perUser, bool policyOff, string? offered, bool disabled, bool withdrawn, string processor, FixOffer offer)
    {
        var running = ReleaseVersion.Parse("1.2.0")!;
        var msi = new Package(new Uri("https://u/v1/thermi-1.3.0-x64.msi"), 40_000_000, new string('a', 64));
        var manifest = offered is null ? null : new Manifest
        {
            Channel = Manifest.Stable,
            Sequence = 1,
            Version = ReleaseVersion.Parse(offered),
            Disabled = disabled,
            Withdrawn = withdrawn ? [running] : [],
            Msis = new Dictionary<string, Package> { ["x64"] = msi },
        };

        var (kind, package) = InstallForEveryone.Offer(perUser, policyOff, manifest, running, processor);

        Assert.Equal((offer, offer == FixOffer.MachineMsi ? msi : null), (kind, package));
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

    // The machine's overlay removes this user's own per-user thermi, and nothing else installed.
    [Fact]
    public void OnlyTheUsersOwnPerUserProductIsRemoved()
    {
        InstalledProduct[] products =
        [
            new("{M}", InstallForEveryone.MachineUpgradeCode, ProductContext.Machine, null),
            new("{O}", InstallForEveryone.PerUserUpgradeCode, ProductContext.UserUnmanaged, "S-1-5-21-1-2-3-1002"),
            new("{G}", InstallForEveryone.PerUserUpgradeCode, ProductContext.UserManaged, User),
            new("{X}", "{00000000-0000-0000-0000-000000000000}", ProductContext.UserUnmanaged, User),
            new("{U}", InstallForEveryone.PerUserUpgradeCode, ProductContext.UserUnmanaged, User),
        ];

        Assert.Equal("{U}", InstallForEveryone.PerUserProduct(products, User));
        Assert.Null(InstallForEveryone.PerUserProduct(products[..4], User));
    }

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
