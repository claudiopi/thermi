using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Thermi.Releases.Linux;

/// <summary>
/// A .deb's control stanza, as dpkg reads it: the <c>control</c> file of its <c>control.tar.gz</c>, the second member of
/// the ar archive a .deb is. nfpm, which packs thermi's, writes it gzipped.
/// </summary>
internal static class DebFile
{
    private const string Magic = "!<arch>\n";

    // An ar member's header: its name, 16 bytes, then its time, owner, group and mode, then its size in decimal, 10 bytes.
    private const int HeaderSize = 60;

    private const int SizeAt = 48;

    /// <summary>The control stanza's text, its fields in the order the package wrote them, with no blank line after.</summary>
    public static string Control(string path)
    {
        using var deb = File.OpenRead(path);
        var magic = new byte[Magic.Length];
        deb.ReadExactly(magic);
        if (Encoding.ASCII.GetString(magic) != Magic)
        {
            throw new InvalidDataException($"{path} is no .deb: it is not an ar archive.");
        }

        var header = new byte[HeaderSize];
        while (deb.ReadAtLeast(header, HeaderSize, throwOnEndOfStream: false) == HeaderSize)
        {
            var name = Encoding.ASCII.GetString(header, 0, 16).TrimEnd(' ', '/');
            var size = long.Parse(Encoding.ASCII.GetString(header, SizeAt, 10).Trim(), System.Globalization.CultureInfo.InvariantCulture);
            if (name == "control.tar.gz")
            {
                var member = new byte[size];
                deb.ReadExactly(member);
                using var gzip = new GZipStream(new MemoryStream(member), CompressionMode.Decompress);
                return FromTar(gzip, path);
            }

            // Each member starts at an even offset.
            deb.Seek(size + (size % 2), SeekOrigin.Current);
        }

        throw new InvalidDataException($"{path} has no control.tar.gz.");
    }

    private static string FromTar(Stream tar, string path)
    {
        using var reader = new TarReader(tar);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry.Name is "./control" or "control" && entry.DataStream is { } data)
            {
                using var text = new StreamReader(data, Encoding.UTF8);
                return text.ReadToEnd().TrimEnd('\n');
            }
        }

        throw new InvalidDataException($"{path}'s control.tar.gz has no control file.");
    }
}
