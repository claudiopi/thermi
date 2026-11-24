using System.Text;

namespace Thermi.Core.Search;

/// <summary>A word of a text: where it starts in the text and its comparable form, as long as it shows there.</summary>
internal readonly record struct Word(int At, string Text);

/// <summary>
/// How every search of the window compares what is typed with what it finds, so the same words find the same in each:
/// without accents or case, by words cut at anything that is no letter or digit and where digits follow letters, as
/// "RTX5060" reads "rtx 5060" and "Wi-Fi" "wi fi".
/// </summary>
internal static class SearchWords
{
    /// <summary>The comparable words of a text, each with where it starts in it.</summary>
    public static IReadOnlyList<Word> In(string text)
    {
        var folded = Fold(text);
        var words = new List<Word>();
        var start = -1;
        for (var at = 0; at <= folded.Length; at++)
        {
            var inWord = at < folded.Length && char.IsLetterOrDigit(folded[at]);
            var cut = inWord && start >= 0 && char.IsDigit(folded[at]) && char.IsLetter(folded[at - 1]);
            if (start >= 0 && (!inWord || cut))
            {
                words.Add(new Word(start, folded[start..at]));
                start = -1;
            }

            if (inWord && start < 0)
            {
                start = at;
            }
        }

        return words;
    }

    /// <summary>The comparable words typed, in order.</summary>
    public static IReadOnlyList<string> Typed(string query) => [.. In(query).Select(word => word.Text)];

    // Each character as its letter without accents, in lower case, one for one, so a word's place in the folded text is
    // its place in the text as it shows, which is what a mark needs: decomposing the whole text would move what follows
    // an accent. A surrogate pair stays as it is, since folding half of one would break it.
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            folded.Append(character < 128 || char.IsSurrogate(character)
                ? char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character.ToString().Normalize(NormalizationForm.FormD)[0]));
        }

        return folded.ToString();
    }
}
