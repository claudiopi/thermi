using System.Text;

namespace Thermi.Core.Tray;

/// <summary>
/// The access keys of one menu's entries, as Windows' menus mark them with "&amp;", so each entry is chosen with one key in
/// any language: in the entries' order, each takes the first letter of one of its words that no entry before it took, else
/// any other letter or digit of it, else none. A grayed entry, which nothing chooses, takes none. An "&amp;" of the text
/// itself is doubled, so it shows as one.
/// </summary>
public static class AccessKeys
{
    public static IReadOnlyList<string> Mark(IReadOnlyList<string> texts, IReadOnlyList<bool>? grayed = null)
    {
        var taken = new HashSet<char>();
        return [.. texts.Select((text, at) => Marked(text, grayed?[at] == true ? null : taken))];
    }

    private static string Marked(string text, HashSet<char>? taken)
    {
        var key = -1;
        foreach (var at in WordStarts(text).Concat(Enumerable.Range(0, text.Length).Where(at => char.IsLetterOrDigit(text[at]))))
        {
            if (taken?.Add(char.ToUpperInvariant(text[at])) == true)
            {
                key = at;
                break;
            }
        }

        var marked = new StringBuilder(text.Length + 2);
        for (var at = 0; at < text.Length; at++)
        {
            if (at == key || text[at] == '&')
            {
                marked.Append('&');
            }

            marked.Append(text[at]);
        }

        return marked.ToString();
    }

    private static IEnumerable<int> WordStarts(string text) =>
        Enumerable.Range(0, text.Length).Where(at => char.IsLetterOrDigit(text[at]) && (at == 0 || !char.IsLetterOrDigit(text[at - 1])));
}
