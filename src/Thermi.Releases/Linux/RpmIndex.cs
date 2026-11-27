using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Thermi.Releases.Linux;

/// <summary>
/// A channel's dnf index, as createrepo writes one: <c>repodata/repomd.xml</c>, which the levers sign, naming by their
/// hashes the package list, <c>primary</c>, each package's files, <c>filelists</c>, and <c>other</c>; each named by its
/// hash too, so a PC that fetched a <c>repomd.xml</c> while the next was being written still finds what it names.
/// </summary>
internal static class RpmIndex
{
#pragma warning disable S5332, S1075 // XML namespaces, names dnf matches as they are, which nothing fetches.
    private static readonly XNamespace Repo = "http://linux.duke.edu/metadata/repo";

    private static readonly XNamespace Common = "http://linux.duke.edu/metadata/common";

    private static readonly XNamespace Rpm = "http://linux.duke.edu/metadata/rpm";

    private static readonly XNamespace FileLists = "http://linux.duke.edu/metadata/filelists";

    private static readonly XNamespace Other = "http://linux.duke.edu/metadata/other";
#pragma warning restore S5332, S1075

    // rpm's sense flags: what a dependency's version must be, and whether its scripts need it before they run.
    private const long Less = 2, Greater = 4, Equal = 8, BeforeScripts = 64 | 512 | 1024;

    private const long Ghost = 64;

    /// <summary>
    /// Writes <c>repodata</c> under <paramref name="output"/>, unsigned, for the .rpms at <paramref name="rpms"/>, each a
    /// path under <paramref name="root"/>, the channel's folder, where dnf finds them.
    /// </summary>
    public static void Write(string root, IReadOnlyList<string> rpms, DateTimeOffset now, string output)
    {
        var packages = rpms.Select(rpm => Package.Of(root, rpm)).ToList();
        var count = packages.Count.ToString(CultureInfo.InvariantCulture);
        var files = new (string Type, XElement Root)[]
        {
            ("primary", new XElement(Common + "metadata", new XAttribute(XNamespace.Xmlns + "rpm", Rpm), new XAttribute("packages", count),
                packages.Select(Primary))),
            ("filelists", new XElement(FileLists + "filelists", new XAttribute("packages", count), packages.Select(Listed))),
            ("other", new XElement(Other + "otherdata", new XAttribute("packages", count),
                packages.Select(package => Identified(Other, package)))),
        };
        var repodata = Directory.CreateDirectory(Path.Combine(output, "repodata")).FullName;
        var stamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var data = new List<XElement>();
        foreach (var (type, element) in files)
        {
            var open = Encoding.UTF8.GetBytes(new XDocument(new XDeclaration("1.0", "UTF-8", null), element).Declaration + "\n" + element);
            var packed = Gzipped(open);
            var name = $"repodata/{Hash(packed)}-{type}.xml.gz";
            File.WriteAllBytes(Path.Combine(output, name), packed);
            data.Add(new XElement(Repo + "data", new XAttribute("type", type),
                new XElement(Repo + "checksum", new XAttribute("type", "sha256"), Hash(packed)),
                new XElement(Repo + "open-checksum", new XAttribute("type", "sha256"), Hash(open)),
                new XElement(Repo + "location", new XAttribute("href", name)),
                new XElement(Repo + "timestamp", stamp),
                new XElement(Repo + "size", packed.Length),
                new XElement(Repo + "open-size", open.Length)));
        }

        var repomd = new XElement(Repo + "repomd", new XAttribute(XNamespace.Xmlns + "rpm", Rpm), new XElement(Repo + "revision", stamp), data);
        File.WriteAllText(Path.Combine(repodata, "repomd.xml"), new XDeclaration("1.0", "UTF-8", null) + "\n" + repomd + "\n");
    }

    private static XElement Primary(Package package)
    {
        var rpm = package.Header;
        var format = new XElement(Common + "format",
            new XElement(Rpm + "license", rpm.Text(RpmTag.License)),
            new XElement(Rpm + "vendor", rpm.Text(RpmTag.Vendor)),
            new XElement(Rpm + "group", rpm.Text(RpmTag.Group)),
            new XElement(Rpm + "buildhost", rpm.Text(RpmTag.BuildHost)),
            new XElement(Rpm + "sourcerpm", rpm.Text(RpmTag.SourceRpm)),
            new XElement(Rpm + "header-range", new XAttribute("start", rpm.HeaderStart), new XAttribute("end", rpm.HeaderEnd)),
            Dependencies(rpm, "provides", RpmTag.ProvideName, RpmTag.ProvideFlags, RpmTag.ProvideVersion),
            Dependencies(rpm, "requires", RpmTag.RequireName, RpmTag.RequireFlags, RpmTag.RequireVersion),
            Dependencies(rpm, "conflicts", RpmTag.ConflictName, RpmTag.ConflictFlags, RpmTag.ConflictVersion),
            Dependencies(rpm, "obsoletes", RpmTag.ObsoleteName, RpmTag.ObsoleteFlags, RpmTag.ObsoleteVersion),
            Dependencies(rpm, "recommends", RpmTag.RecommendName, RpmTag.RecommendFlags, RpmTag.RecommendVersion),
            // The files dnf resolves a dependency on a path against without the file lists, as createrepo picks them.
            package.Files.Where(file => file.Path.StartsWith("/etc/", StringComparison.Ordinal) || file.Path.Contains("bin/", StringComparison.Ordinal))
                .Select(file => FileEntry(Common, file)));
        return new XElement(Common + "package", new XAttribute("type", "rpm"),
            new XElement(Common + "name", rpm.Text(RpmTag.Name)),
            new XElement(Common + "arch", rpm.Text(RpmTag.Arch)),
            Version(Common, rpm),
            new XElement(Common + "checksum", new XAttribute("type", "sha256"), new XAttribute("pkgid", "YES"), package.Id),
            new XElement(Common + "summary", rpm.Text(RpmTag.Summary)),
            new XElement(Common + "description", rpm.Text(RpmTag.Description)),
            new XElement(Common + "packager", rpm.Text(RpmTag.Packager)),
            new XElement(Common + "url", rpm.Text(RpmTag.Url)),
            new XElement(Common + "time", new XAttribute("file", package.Time), new XAttribute("build", First(rpm, RpmTag.BuildTime))),
            new XElement(Common + "size", new XAttribute("package", package.Length),
                new XAttribute("installed", rpm.Numbers(RpmTag.LongSize) is [var size, ..] ? size : First(rpm, RpmTag.Size)),
                new XAttribute("archive", First(rpm, RpmTag.ArchiveSize))),
            new XElement(Common + "location", new XAttribute("href", package.Location)),
            format);
    }

    private static XElement Listed(Package package)
    {
        var listed = Identified(FileLists, package);
        listed.Add(package.Files.Select(file => FileEntry(FileLists, file)));
        return listed;
    }

    private static XElement Identified(XNamespace space, Package package) => new(space + "package",
        new XAttribute("pkgid", package.Id), new XAttribute("name", package.Header.Text(RpmTag.Name)!),
        new XAttribute("arch", package.Header.Text(RpmTag.Arch)!), Version(space, package.Header));

    private static XElement FileEntry(XNamespace space, (string Path, string? Type) file) =>
        new(space + "file", file.Type is null ? null : new XAttribute("type", file.Type), file.Path);

    private static XElement Version(XNamespace space, RpmFile rpm) => new(space + "version",
        new XAttribute("epoch", First(rpm, RpmTag.Epoch)), new XAttribute("ver", rpm.Text(RpmTag.Version)!), new XAttribute("rel", rpm.Text(RpmTag.Release)!));

    private static XElement? Dependencies(RpmFile rpm, string kind, RpmTag names, RpmTag flags, RpmTag versions)
    {
        var entries = rpm.Texts(names).Select((name, at) => (Name: name, Flags: rpm.Numbers(flags)[at], Version: rpm.Texts(versions)[at]))
            // What rpm itself provides, as rpmlib's features, no repository resolves.
            .Where(entry => !entry.Name.StartsWith("rpmlib(", StringComparison.Ordinal))
            .Distinct()
            .Select(Entry)
            .ToList();
        return entries.Count == 0 ? null : new XElement(Rpm + kind, entries);
    }

    // A dependency as primary names it: its name, how its version compares, and the epoch, version and release it names.
    private static XElement Entry((string Name, long Flags, string Version) dependency)
    {
        var entry = new XElement(Rpm + "entry", new XAttribute("name", dependency.Name));
        var compares = (dependency.Flags & (Less | Greater | Equal)) switch
        {
            Less => "LT",
            Greater => "GT",
            Equal => "EQ",
            Less | Equal => "LE",
            Greater | Equal => "GE",
            _ => null,
        };
        if (compares is not null && dependency.Version.Length > 0)
        {
            var epoch = dependency.Version.Contains(':', StringComparison.Ordinal) ? dependency.Version.Split(':', 2)[0] : "0";
            var rest = dependency.Version.Contains(':', StringComparison.Ordinal) ? dependency.Version.Split(':', 2)[1] : dependency.Version;
            var parts = rest.Split('-', 2);
            entry.Add(new XAttribute("flags", compares), new XAttribute("epoch", epoch), new XAttribute("ver", parts[0]));
            if (parts.Length == 2)
            {
                entry.Add(new XAttribute("rel", parts[1]));
            }
        }

        if ((dependency.Flags & BeforeScripts) != 0)
        {
            entry.Add(new XAttribute("pre", "1"));
        }

        return entry;
    }

    private static long First(RpmFile rpm, RpmTag tag) => rpm.Numbers(tag) is [var first, ..] ? first : 0;

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

    /// <summary>A package of the channel: its header, its file's hash and size, and its files, each a folder or not.</summary>
    private sealed record Package(RpmFile Header, string Id, long Length, long Time, string Location, IReadOnlyList<(string Path, string? Type)> Files)
    {
        public static Package Of(string root, string location)
        {
            var path = System.IO.Path.Combine(root, location);
            var bytes = System.IO.File.ReadAllBytes(path);
            var rpm = RpmFile.Read(bytes);
            var dirs = rpm.Texts(RpmTag.DirNames);
            var indexes = rpm.Numbers(RpmTag.DirIndexes);
            var modes = rpm.Numbers(RpmTag.FileModes);
            var flags = rpm.Numbers(RpmTag.FileFlags);
            var files = rpm.Texts(RpmTag.BaseNames).Select((name, at) => (dirs[(int)indexes[at]] + name,
                (modes[at] & 0xF000) == 0x4000 ? "dir" : (flags[at] & Ghost) != 0 ? "ghost" : null)).ToList();
            return new Package(rpm, Hash(bytes), bytes.Length, new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds(),
                location.Replace('\\', '/'), files);
        }
    }
}
