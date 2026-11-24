namespace Thermi.Core.Search;

/// <summary>A word typed with one letter wrong, as "tempreature" for "temperature", which a search forgives once nothing matches.</summary>
internal static class Typo
{
    /// <summary>Under this many letters, one letter changed is another word already: "fam" is no "fan".</summary>
    public const int MinLength = 5;

    /// <summary>
    /// How many letters of the start of <paramref name="word"/> <paramref name="typed"/> is, with one letter added, dropped,
    /// changed or two side by side swapped; 0 when it is none, or shorter than <see cref="MinLength"/>. The start and not the
    /// whole word, since a search runs while the word is still being typed.
    /// </summary>
    public static int Head(string word, string typed)
    {
        if (typed.Length < MinLength)
        {
            return 0;
        }

        foreach (var length in new[] { typed.Length, typed.Length - 1, typed.Length + 1 })
        {
            var head = word[..Math.Min(length, word.Length)];
            if (head.Length >= typed.Length - 1 && AtMostOneEdit(head, typed))
            {
                return head.Length;
            }
        }

        return 0;
    }

    // Damerau-Levenshtein distance of at most one.
    private static bool AtMostOneEdit(string one, string other)
    {
        if (Math.Abs(one.Length - other.Length) > 1)
        {
            return false;
        }

        var same = 0;
        while (same < one.Length && same < other.Length && one[same] == other[same])
        {
            same++;
        }

        if (one.Length == other.Length)
        {
            return same >= one.Length - 1
                || one[(same + 1)..] == other[(same + 1)..]
                || (one[same] == other[same + 1] && one[same + 1] == other[same] && one[(same + 2)..] == other[(same + 2)..]);
        }

        return one.Length > other.Length ? one[(same + 1)..] == other[same..] : one[same..] == other[(same + 1)..];
    }
}
