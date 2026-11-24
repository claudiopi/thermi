namespace Thermi.Core.Search;

/// <summary>
/// The window's one search, over findables read once: every word typed must start a word of a findable, in any order,
/// and the best come first, the title starting with what was typed, then every word in the title, then some, then only
/// through the detail, an alias or a hidden word, and last, once nothing matched as typed, a word with one letter wrong.
/// Within one quality the findables keep their order. Each found one says which spans of its texts matched, so the row
/// shows why it is there.
/// </summary>
public sealed class SearchIndex(IReadOnlyList<Findable> among)
{
    private readonly Entry[] _entries = [.. among.Select(findable => new Entry(findable))];

    // How a word typed matched a findable, best first; a typo only once nothing matched as typed.
    private enum Level
    {
        Title,
        Other,
        Typo,
        None,
    }

    /// <summary>The findables every word of <paramref name="query"/> finds, best first; with no word typed, all of them, as they came.</summary>
    public IReadOnlyList<Found> Find(string query)
    {
        var typed = SearchWords.Typed(query);
        var found = Ranked(typed, tolerate: false);
        return found.Count > 0 || !typed.Any(word => word.Length >= Typo.MinLength) ? found : Ranked(typed, tolerate: true);
    }

    /// <summary>The spans of <paramref name="text"/> that the words of <paramref name="query"/> start, whether or not all of them are there.</summary>
    public static IReadOnlyList<Mark> MarksIn(string text, string query) => Entry.Marks(SearchWords.In(text), SearchWords.Typed(query), []);

    private List<Found> Ranked(IReadOnlyList<string> typed, bool tolerate) =>
    [
        .. _entries
            .Select((entry, index) => (Entry: entry, Index: index, Levels: typed.Select(word => entry.LevelOf(word, tolerate)).ToList()))
            .Where(each => !each.Levels.Contains(Level.None))
            .OrderBy(each => Quality(each.Entry, typed, each.Levels))
            .Select(each => each.Entry.Found(each.Index, typed, each.Levels)),
    ];

    // 0 the title starts with what was typed, 1 every word is in the title, 2 some are, 3 none, 4 one needed a typo forgiven.
    private static int Quality(Entry entry, IReadOnlyList<string> typed, List<Level> levels) =>
        typed.Count == 0 ? 0
        : levels.Contains(Level.Typo) ? 4
        : entry.TitleJoined.StartsWith(string.Join(' ', typed), StringComparison.Ordinal) ? 0
        : levels.TrueForAll(level => level == Level.Title) ? 1
        : levels.Contains(Level.Title) ? 2
        : 3;

    private sealed class Entry(Findable findable)
    {
        private readonly IReadOnlyList<Word> _title = SearchWords.In(findable.Title);
        private readonly IReadOnlyList<Word> _detail = SearchWords.In(findable.Detail);
        private readonly IReadOnlyList<Word>[] _aliases = [.. findable.Aliases.Select(SearchWords.In)];
        private readonly string[] _others = [.. findable.Words.SelectMany(SearchWords.In).Select(word => word.Text)];

        public string TitleJoined { get; } = string.Join(' ', SearchWords.In(findable.Title).Select(word => word.Text));

        public Level LevelOf(string typed, bool tolerate) =>
            Starts(_title, typed) ? Level.Title
            : Starts(_detail, typed)
                || Array.Exists(_aliases, alias => Starts(alias, typed))
                || Array.Exists(_others, word => word.StartsWith(typed, StringComparison.Ordinal)) ? Level.Other
            : tolerate && AllWords().Any(word => Typo.Head(word, typed) > 0) ? Level.Typo
            : Level.None;

        public Found Found(int index, IReadOnlyList<string> typed, List<Level> levels)
        {
            // An alias explains the row where a word typed is not in its title: the first alias that holds such a word.
            var unexplained = typed.Where((_, at) => levels[at] != Level.Title).ToList();
            var alias = Array.FindIndex(_aliases, words => unexplained.Exists(word => Starts(words, word)));
            var forgiven = typed.Where((_, at) => levels[at] == Level.Typo).ToHashSet(StringComparer.Ordinal);
            return new Found(
                index,
                Marks(_title, typed, forgiven),
                Marks(_detail, typed, forgiven),
                alias < 0 ? null : findable.Aliases[alias],
                alias < 0 ? [] : Marks(_aliases[alias], typed, forgiven));
        }

        private static bool Starts(IReadOnlyList<Word> words, string typed) => words.Any(word => word.Text.StartsWith(typed, StringComparison.Ordinal));

        private IEnumerable<string> AllWords() =>
            _title.Concat(_detail).Concat(_aliases.SelectMany(words => words)).Select(word => word.Text).Concat(_others);

        // Each word typed marks the start of every word it starts, or with a typo forgiven the start it stands for, merged
        // where they touch.
        public static List<Mark> Marks(IReadOnlyList<Word> words, IReadOnlyList<string> typed, HashSet<string> forgiven)
        {
            var spans = words
                .SelectMany(word => typed.Select(each => (
                    word.At,
                    Length: word.Text.StartsWith(each, StringComparison.Ordinal) ? each.Length
                        : forgiven.Contains(each) ? Typo.Head(word.Text, each)
                        : 0)))
                .Where(span => span.Length > 0)
                .OrderBy(span => span.At)
                .ToList();
            var marks = new List<Mark>();
            foreach (var (at, length) in spans)
            {
                if (marks.Count > 0 && at <= marks[^1].To)
                {
                    marks[^1] = marks[^1] with { To = Math.Max(marks[^1].To, at + length) };
                }
                else
                {
                    marks.Add(new Mark(at, at + length));
                }
            }

            return marks;
        }
    }
}
