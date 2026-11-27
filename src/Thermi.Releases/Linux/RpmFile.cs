using System.Buffers.Binary;
using System.Text;

namespace Thermi.Releases.Linux;

/// <summary>
/// What an .rpm's header says of it, as rpm lays a package out: a 96-byte lead, the signature's header, padded to 8 bytes,
/// then the package's header, each an index of tags over a store of their values, big-endian; then the payload. The
/// repository's index names where the package's header starts and ends, so dnf can fetch it alone.
/// </summary>
internal sealed class RpmFile
{
    private const int LeadSize = 96;

    private const int IndexEntrySize = 16;

    private const int StringType = 6;

    private const int StringArrayType = 8;

    private const int I18NStringType = 9;

    private readonly byte[] _bytes;

    private readonly Dictionary<int, (int Type, int Offset, int Count)> _tags = [];

    private readonly int _store;

    private RpmFile(byte[] bytes)
    {
        _bytes = bytes;
        if (bytes.Length < LeadSize || BinaryPrimitives.ReadUInt32BigEndian(bytes) != 0xEDABEEDB)
        {
            throw new InvalidDataException("It is no .rpm: its lead's magic is missing.");
        }

        var signatureEnd = EndOf(LeadSize);
        HeaderStart = signatureEnd + ((8 - (signatureEnd % 8)) % 8);
        HeaderEnd = EndOf(HeaderStart);
        var count = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(HeaderStart + 8));
        _store = HeaderStart + IndexEntrySize + (count * IndexEntrySize);
        for (var at = 0; at < count; at++)
        {
            var entry = bytes.AsSpan(HeaderStart + IndexEntrySize + (at * IndexEntrySize));
            _tags[BinaryPrimitives.ReadInt32BigEndian(entry)] = (BinaryPrimitives.ReadInt32BigEndian(entry[4..]),
                BinaryPrimitives.ReadInt32BigEndian(entry[8..]), BinaryPrimitives.ReadInt32BigEndian(entry[12..]));
        }
    }

    /// <summary>Where the package's header starts in the file, after the lead and the signature.</summary>
    public int HeaderStart { get; }

    /// <summary>Where the package's header ends, and its payload starts.</summary>
    public int HeaderEnd { get; }

    public static RpmFile Read(byte[] bytes) => new(bytes);

    /// <summary>A tag's text, the first of a list or of its translations, or null when the package has none.</summary>
    public string? Text(RpmTag tag) => Texts(tag) is [var first, ..] ? first : null;

    /// <summary>A tag's texts, or none when the package has the tag not.</summary>
    public IReadOnlyList<string> Texts(RpmTag tag)
    {
        if (!_tags.TryGetValue((int)tag, out var found) || found.Type is not (StringType or StringArrayType or I18NStringType))
        {
            return [];
        }

        var texts = new List<string>();
        var at = _store + found.Offset;
        for (var each = 0; each < (found.Type == StringType ? 1 : found.Count); each++)
        {
            var end = Array.IndexOf(_bytes, (byte)0, at);
            texts.Add(Encoding.UTF8.GetString(_bytes, at, end - at));
            at = end + 1;
        }

        return texts;
    }

    /// <summary>A tag's 32-bit numbers, or none when the package has the tag not.</summary>
    public IReadOnlyList<long> Numbers(RpmTag tag)
    {
        if (!_tags.TryGetValue((int)tag, out var found))
        {
            return [];
        }

        // rpm's types 3, 4 and 5 are 16, 32 and 64 bits wide.
        var width = found.Type switch { 3 => 2, 4 => 4, 5 => 8, _ => 0 };
        return [.. Enumerable.Range(0, width == 0 ? 0 : found.Count).Select(each =>
        {
            var value = _bytes.AsSpan(_store + found.Offset + (each * width));
            return width switch
            {
                2 => BinaryPrimitives.ReadUInt16BigEndian(value),
                4 => BinaryPrimitives.ReadUInt32BigEndian(value),
                _ => BinaryPrimitives.ReadInt64BigEndian(value),
            };
        })];
    }

    // A header's end: its magic and reserved bytes, its counts of entries and of the store's bytes, the entries, the store.
    private int EndOf(int start)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(start)) != 0x8EADE801)
        {
            throw new InvalidDataException($"It is no .rpm: no header's magic at {start}.");
        }

        return start + IndexEntrySize + (BinaryPrimitives.ReadInt32BigEndian(_bytes.AsSpan(start + 8)) * IndexEntrySize)
            + BinaryPrimitives.ReadInt32BigEndian(_bytes.AsSpan(start + 12));
    }
}

/// <summary>The tags of an .rpm's header that a repository's index says, by rpm's own numbers.</summary>
internal enum RpmTag
{
    Name = 1000,
    Version = 1001,
    Release = 1002,
    Epoch = 1003,
    Summary = 1004,
    Description = 1005,
    BuildTime = 1006,
    BuildHost = 1007,
    Size = 1009,
    Vendor = 1011,
    License = 1014,
    Packager = 1015,
    Group = 1016,
    Url = 1020,
    Arch = 1022,
    FileModes = 1030,
    FileFlags = 1037,
    SourceRpm = 1044,
    ArchiveSize = 1046,
    ProvideName = 1047,
    RequireFlags = 1048,
    RequireName = 1049,
    RequireVersion = 1050,
    ConflictFlags = 1053,
    ConflictName = 1054,
    ConflictVersion = 1055,
    ObsoleteName = 1090,
    ProvideFlags = 1112,
    ProvideVersion = 1113,
    ObsoleteFlags = 1114,
    ObsoleteVersion = 1115,
    DirIndexes = 1116,
    BaseNames = 1117,
    DirNames = 1118,
    LongSize = 5009,
    RecommendName = 5046,
    RecommendVersion = 5047,
    RecommendFlags = 5048,
}
