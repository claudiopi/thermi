using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Resources;
using System.Text;

namespace Thermi.Core.Translations;

/// <summary>Marks an enum whose every member has a text in every translation, keyed "Enum.Member". A repo rule checks it.</summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class TranslatedAttribute : Attribute;

/// <summary>
/// Every text a user reads, in one language. Each is an enum member marked <see cref="TranslatedAttribute"/>, so code
/// asks only for texts that exist. They live in <c>Translations/Texts.resx</c>, the English one, and a file per language
/// beside it, which a translator edits as plain XML or in any resource editor; a text a translation lacks is English.
/// Numbers are formatted by the caller's regional format, never by the language.
/// </summary>
public sealed class Texts
{
    public const string Fallback = "en";

    /// <summary>How much longer than English every pseudo-language text is.</summary>
    public const double PseudoGrowth = 0.4;

    private const string Accents = "áéíóúýÁÉÍÓÚÝçñÇÑ";
    private const string Plain = "aeiouyAEIOUYcnCN";
    private const string Padding = "ŵĥŷţĩŏŭǩĺžđġ";

    private static readonly ResourceManager Resources = new("Thermi.Core.Translations.Texts", typeof(Texts).Assembly);
    private static readonly ConcurrentDictionary<string, Texts> Loaded = new(StringComparer.Ordinal);

    private readonly FrozenDictionary<string, string> _texts;

    private Texts(FrozenDictionary<string, string> texts)
    {
        _texts = texts;
    }

    /// <summary>The languages thermi ships, as codes.</summary>
    public static IReadOnlyList<string> Languages { get; } = [Fallback, "es"];

    public static Texts English => Of(Fallback);

    /// <summary>The texts of a language thermi ships, or English for any other.</summary>
    public static Texts Of(string language) =>
        Loaded.GetOrAdd(Supported(language) ?? Fallback, code => new Texts(Read(CultureInfo.GetCultureInfo(code))));

    /// <summary>The language chosen in the settings when thermi has it, else Windows' display language when it does, else English.</summary>
    public static string Pick(string? chosen, CultureInfo windows) => Supported(chosen) ?? Supported(windows.Name) ?? Fallback;

    public static Texts For(string? chosen, CultureInfo windows) => Of(Pick(chosen, windows));

    /// <summary>
    /// English made <see cref="PseudoGrowth"/> longer with accented letters and put in brackets, to see what longer texts
    /// do to the window; a text cut short loses its closing bracket. It has no file, so it has no keys to check.
    /// </summary>
    public static Texts Pseudo() => new(English._texts.ToFrozenDictionary(pair => pair.Key, pair => Stretch(pair.Value), StringComparer.Ordinal));

    /// <summary>Every text by its key, as the window's page takes them.</summary>
    public IReadOnlyDictionary<string, string> All => _texts;

    public static string Key<T>(T text)
        where T : struct, Enum => string.Concat(typeof(T).Name, ".", text.ToString());

    public string Of<T>(T text)
        where T : struct, Enum => _texts[Key(text)];

    /// <summary>The words of a choice whose type is known only as an enum, or null when its type has no translation.</summary>
    public string? OfChoice(Enum choice) => _texts.GetValueOrDefault(string.Concat(choice.GetType().Name, ".", choice.ToString()));

    /// <summary>A text with its blanks filled, numbers in the regional format.</summary>
    public string Format<T>(IFormatProvider numbers, T text, params object?[] values)
        where T : struct, Enum => string.Format(numbers, Of(text), values);

    // A language is known by its code alone: "es-UY" and "es" are Spanish.
    private static string? Supported(string? language)
    {
        var code = language?.Split('-')[0];
        return Languages.FirstOrDefault(supported => string.Equals(supported, code, StringComparison.OrdinalIgnoreCase));
    }

    // English names every text; another language may lack one, and the resource manager then gives English's.
    private static FrozenDictionary<string, string> Read(CultureInfo language) =>
        Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToFrozenDictionary(key => key, key => Resources.GetString(key, language)!, StringComparer.Ordinal);

    // The blanks a text is filled with, as "{0}", keep their letters.
    private static string Stretch(string text)
    {
        var stretched = new StringBuilder("[", text.Length * 2);
        var inBlank = false;
        foreach (var letter in text)
        {
            inBlank = letter == '{' || (inBlank && letter != '}');
            var at = Plain.IndexOf(letter, StringComparison.Ordinal);
            stretched.Append(inBlank || at < 0 ? letter : Accents[at]);
        }

        var padding = (int)Math.Ceiling(text.Length * PseudoGrowth);
        stretched.Append(' ');
        for (var index = 0; index < padding; index++)
        {
            stretched.Append(Padding[index % Padding.Length]);
        }

        return stretched.Append(']').ToString();
    }
}
