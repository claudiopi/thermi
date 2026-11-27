using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Thermi.Core.Updates;

namespace Thermi.Releases.Linux;

/// <summary>
/// A channel's apt index, as apt reads a repository: for each processor, a <c>Packages</c> file with the stanza of each .deb
/// the channel offers, and the suite's <c>Release</c>, which names each index by its hash and which the levers sign.
/// Each index is also kept under its hash, as <c>Acquire-By-Hash</c> says, so a PC that fetched a <c>Release</c> while the
/// next was being written still finds the index it names.
/// </summary>
internal static class AptIndex
{
    public static readonly IReadOnlyList<string> Architectures = ["amd64", "arm64"];

    /// <summary>
    /// Writes <c>dists/&lt;channel&gt;</c> under <paramref name="output"/>, unsigned, for the .debs at
    /// <paramref name="debs"/>, each a path under <paramref name="root"/>, where apt finds them; the release is good for
    /// as long as a manifest is (<see cref="Manifest.Lifetime"/>), until it is signed again.
    /// </summary>
    public static void Write(string root, IReadOnlyList<string> debs, string channel, DateTimeOffset now, string output)
    {
        var suite = Path.Combine(output, "dists", channel);
        var stanzas = debs.Select(deb => Stanza(root, deb)).ToList();
        var indexes = new List<(string Name, byte[] Bytes)>();
        foreach (var architecture in Architectures)
        {
            var text = string.Concat(stanzas.Where(stanza => stanza.Architecture == architecture).Select(stanza => stanza.Text + "\n\n"));
            var plain = Encoding.UTF8.GetBytes(text);
            indexes.Add(($"main/binary-{architecture}/Packages", plain));
            indexes.Add(($"main/binary-{architecture}/Packages.gz", Gzipped(plain)));
        }

        foreach (var (name, bytes) in indexes)
        {
            Save(Path.Combine(suite, name), bytes);
            Save(Path.Combine(suite, Path.GetDirectoryName(name)!, "by-hash", "SHA256", Hash(bytes)), bytes);
        }

        Save(Path.Combine(suite, "Release"), Encoding.UTF8.GetBytes(Release(channel, now, indexes)));
    }

    private static string Release(string channel, DateTimeOffset now, List<(string Name, byte[] Bytes)> indexes)
    {
        var release = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Origin: thermi\nLabel: thermi\nSuite: {channel}\nCodename: {channel}\n")
            .Append(CultureInfo.InvariantCulture, $"Date: {Rfc2822(now)}\nValid-Until: {Rfc2822(now + Manifest.Lifetime)}\n")
            .Append(CultureInfo.InvariantCulture, $"Architectures: {string.Join(' ', Architectures)}\nComponents: main\n")
            .Append(CultureInfo.InvariantCulture, $"Description: thermi's {channel} channel\nAcquire-By-Hash: yes\nSHA256:\n");
        foreach (var (name, bytes) in indexes)
        {
            release.Append(CultureInfo.InvariantCulture, $" {Hash(bytes)} {bytes.Length} {name}\n");
        }

        return release.ToString();
    }

    private static (string Architecture, string Text) Stanza(string root, string deb)
    {
        var control = DebFile.Control(Path.Combine(root, deb));
        var architecture = control.Split('\n').Select(line => line.Split(':', 2)).FirstOrDefault(field => field[0] == "Architecture")?[1].Trim()
            ?? throw new InvalidDataException($"{deb} names no architecture.");
        var bytes = File.ReadAllBytes(Path.Combine(root, deb));
        return (architecture, $"{control}\nFilename: {deb.Replace('\\', '/')}\nSize: {bytes.Length}\nSHA256: {Hash(bytes)}");
    }

    private static string Rfc2822(DateTimeOffset time) => time.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Gzipped(byte[] plain)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(plain);
        }

        return packed.ToArray();
    }

    private static void Save(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
