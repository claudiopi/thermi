using System.Security.Cryptography;
using Thermi.Core.Updates;
using Thermi.Releases;

namespace Thermi.Windows.Tests.Updates;

/// <summary>The owner's channel folder: a lever signs only what a PC opens, and never over a history it cannot trust.</summary>
public sealed class ChannelFolderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("thermi-channels-").FullName;
    private readonly ECDsa _release = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _backup = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [Fact]
    public void ARevokedChannelTakesOnlyTheBackupKey()
    {
        var folder = Folder();
        var before = Published();
        var after = Levers.Revoke(before, Manifest.Stable, new string('b', 64), Now).Channels;

        Assert.NotNull(folder.Refuses(before, after, _release));
        Assert.Null(folder.Refuses(before, after, _backup));
        Assert.Null(folder.Refuses(Empty(), before, _release));
    }

    // A history neither key signed, or gone while its channel serves, stops every lever; a channel never published starts empty.
    [Theory]
    [InlineData("planted")]
    [InlineData("gone")]
    [InlineData("new")]
    public void ALeverSignsOnlyOverAHistoryItTrusts(string history)
    {
        var folder = Folder();
        var published = Published();
        if (history == "planted")
        {
            using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            folder.Write(Empty(), published, _release);
            File.WriteAllBytes(Path.Combine(_root, "history", Manifest.Stable + ".json"), published[Manifest.Stable].Seal(stranger));
        }
        else if (history == "gone")
        {
            folder.Write(Empty(), published, _release);
            File.Delete(Path.Combine(_root, "history", Manifest.Stable + ".json"));
        }

        if (history == "new")
        {
            Assert.All(folder.Read().Values, read => Assert.Null(read.Current));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(folder.Read);
        }
    }

    public void Dispose()
    {
        _release.Dispose();
        _backup.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static Dictionary<string, ChannelHistory> Empty() =>
        Manifest.Channels.ToDictionary(channel => channel, _ => new ChannelHistory([]), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, ChannelHistory> Published()
    {
        var zip = new Package(new Uri("https://updates.thermi.app/v1/thermi-x.zip"), 35_000_000, new string('a', 64));
        var zips = new Dictionary<string, Package>(StringComparer.Ordinal) { ["x64"] = zip };
        return Levers.Publish(Empty(), Manifest.Stable, ReleaseVersion.Parse("1.0.0")!, zips, 100, urgent: false, Now).Channels;
    }

    private ChannelFolder Folder() => new(_root, _release.ExportSubjectPublicKeyInfo(), _backup.ExportSubjectPublicKeyInfo());
}
