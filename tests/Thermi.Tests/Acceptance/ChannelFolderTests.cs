using System.Security.Cryptography;
using Thermi.Core.Updates;
using Thermi.Releases;

namespace Thermi.Tests.Acceptance;

/// <summary>The owner's tools: a lever signs only what a PC opens, never over a history it cannot trust, and keys never over a key.</summary>
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
        Assert.Null(folder.Refuses(after, after, _release));
    }

    // keys writes both pairs or none: never over a key, nor half a pair beside the other's half.
    [Theory]
    [InlineData(null)]
    [InlineData("backup.pub.pem")]
    public void KeysNeverWriteOverAKey(string? there)
    {
        var folder = new DirectoryInfo(Path.Combine(_root, "keys"));
        folder.Create();
        if (there is not null)
        {
            File.WriteAllText(Path.Combine(folder.FullName, there), "kept");
        }

        var refused = KeyFiles.MakeIn(folder);

        Assert.Equal(there is not null, refused is not null);
        Assert.Equal(there is null ? KeyFiles.Names.Count : 1, folder.GetFiles().Length);
    }

    // A history neither key signed, a signed manifest in its place, or none while its channel serves stops every lever; a new channel starts empty.
    [Theory]
    [InlineData("planted")]
    [InlineData("a manifest")]
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
        else if (history == "a manifest")
        {
            folder.Write(Empty(), published, _release);
            File.Copy(Path.Combine(_root, Manifest.Stable + ".json"), Path.Combine(_root, "history", Manifest.Stable + ".json"), overwrite: true);
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
