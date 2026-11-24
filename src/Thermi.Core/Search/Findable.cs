using System.Text.Json.Serialization;

namespace Thermi.Core.Search;

/// <summary>A span of a text as it shows, from its first character to the one after its last.</summary>
public sealed record Mark(int From, int To);

/// <summary>
/// What a search can find: the title its row shows, the detail it shows beside or under it, as a sensor's device, the
/// other names it goes by, which its row can show in place of the detail when one of them found it, as the item a sensor
/// feeds, and words that find it without showing, as a type's synonyms.
/// </summary>
public sealed record Findable(string Title, string Detail, IReadOnlyList<string> Aliases, IReadOnlyList<string> Words);

/// <summary>
/// A findable every word of a search found: its place among those searched, the spans of its title and detail that
/// matched, and the alias that explains it where its title does not, with its spans, or null.
/// </summary>
public sealed record Found(
    int Index,
    IReadOnlyList<Mark> Title,
    IReadOnlyList<Mark> Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Alias,
    IReadOnlyList<Mark> AliasMarks);
