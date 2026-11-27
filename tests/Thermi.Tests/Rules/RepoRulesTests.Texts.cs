using System.Text.RegularExpressions;
using System.Xml.Linq;
using Thermi.Core.Translations;

namespace Thermi.Tests.Rules;

/// <summary>The texts a user reads: each a member of a translated enum, in every language, with the same blanks.</summary>
public sealed partial class RepoRulesTests
{
    private static readonly string[] DriverProducts = ["PawnIO", "WinRing0"];

    // Each text is an enum member, so a member without a text, or a text no member asks for, is a broken promise.
    [Fact]
    public void EveryTranslationHasEveryText()
    {
        var folder = Path.Combine(Root, "src", "Thermi.Core", "Translations");
        var english = ResxTexts(Path.Combine(folder, "Texts.resx"));
        var members = typeof(Texts).Assembly.GetTypes()
            .Where(type => type.IsEnum && type.IsDefined(typeof(TranslatedAttribute), inherit: false))
            .SelectMany(type => Enum.GetNames(type).Select(name => $"{type.Name}.{name}"))
            .ToList();
        var translations = Directory.EnumerateFiles(folder, "Texts.*.resx").ToList();

        Assert.NotEmpty(translations);
        AssertNone(members.Except(english.Keys, StringComparer.Ordinal).Select(key => $"Texts.resx has no text for {key}")
            .Concat(english.Keys.Except(members, StringComparer.Ordinal).Select(key => $"Texts.resx has {key}, which no translated enum has"))
            .Concat(TextFindings("Texts.resx", english))
            .Concat(translations.SelectMany(file => TranslationFindings(Relative(file), english, ResxTexts(file), ResxExplained(file)))),
            "give each member of a [Translated] enum its text in src/Thermi.Core/Translations/Texts.resx and every Texts.*.resx, "
            + "named Enum.Member, with the same {0} blanks");
    }

    // A text left as English reads as one the translation forgot, as the broken file's pill did, so the translation says
    // why its words stay, as an abbreviation or a unit, in the text's comment.
    private static IEnumerable<string> TranslationFindings(
        string file,
        Dictionary<string, string?> english,
        Dictionary<string, string?> translation,
        HashSet<string> explained) =>
        english.Keys.Except(translation.Keys, StringComparer.Ordinal).Select(key => $"{file} has no text for {key}")
            .Concat(translation.Keys.Except(english.Keys, StringComparer.Ordinal).Select(key => $"{file} has {key}, which English does not"))
            .Concat(TextFindings(file, translation))
            .Concat(translation
                .Where(pair => english.TryGetValue(pair.Key, out var text) && !Blanks(text ?? "").SetEquals(Blanks(pair.Value ?? "")))
                .Select(pair => $"{file} fills other blanks than English in {pair.Key}"))
            .Concat(translation
                .Where(pair => english.GetValueOrDefault(pair.Key) == pair.Value && Letter().IsMatch(Blank().Replace(pair.Value ?? "", "")))
                .Where(pair => !explained.Contains(pair.Key))
                .Select(pair => $"{file} leaves {pair.Key} as English without saying why in its comment"));

    // A blank text shows nothing where a person needs words, and the driver's product is never named
    // (docs/adr/0010-driver-and-service-from-the-window.md).
    private static IEnumerable<string> TextFindings(string file, Dictionary<string, string?> texts) =>
        texts.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => $"{file} has a blank text for {pair.Key}")
            .Concat(texts.Where(pair => DriverProducts.Any(product => pair.Value?.Contains(product, StringComparison.OrdinalIgnoreCase) == true))
                .Select(pair => $"{file} names the driver's product in {pair.Key}"));

    private static Dictionary<string, string?> ResxTexts(string file) =>
        XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value"), StringComparer.Ordinal);

    private static HashSet<string> ResxExplained(string file) =>
        [.. XDocument.Load(file).Root!.Elements("data")
            .Where(data => !string.IsNullOrWhiteSpace((string?)data.Element("comment")))
            .Select(data => (string)data.Attribute("name")!)];

    private static HashSet<string> Blanks(string text) => [.. Blank().Matches(text).Select(match => match.Value)];

    // A blank a text is filled with, like "{0}" or "{1:N0}".
    [GeneratedRegex(@"\{\d+[^}]*\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Blank();

    [GeneratedRegex(@"\p{L}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Letter();
}
