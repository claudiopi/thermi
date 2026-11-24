using System.Globalization;
using System.Text.RegularExpressions;
using Thermi.Core.Editing;
using Thermi.Core.Translations;

namespace Thermi.Tests;

/// <summary>The texts stretched to test the window's room, as a long translation would.</summary>
public sealed partial class TextTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [GeneratedRegex(@"\{\d+\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Blank();

    [Fact]
    public void ThePseudoLanguageIsLongerAccentedAndKeepsItsBlanks()
    {
        var pseudo = Texts.Pseudo();

        var exit = pseudo.Of(UiText.Exit);
        var name = pseudo.Format(English, UiText.OverlayName, 2);

        Assert.StartsWith("[Éxít ", exit, StringComparison.Ordinal);
        Assert.EndsWith("]", exit, StringComparison.Ordinal);
        Assert.All(Enum.GetValues<Refusal>(), refusal => Assert.True(pseudo.Of(refusal).Length >= Texts.English.Of(refusal).Length * (1 + Texts.PseudoGrowth)));
        Assert.Contains(" 2 ", name, StringComparison.Ordinal);
        Assert.Equal(Blank().Count(Texts.English.Of(Refusal.Invalid)), Blank().Count(pseudo.Of(Refusal.Invalid)));
    }
}
