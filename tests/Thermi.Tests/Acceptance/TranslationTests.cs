using System.Globalization;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Every text a user reads comes from the translations, and numbers follow the regional format. That every translation
/// has every text is a repo rule, in <c>RepoRulesTests</c>.
/// </summary>
public sealed class TranslationTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    [Fact]
    public void TheLanguageFollowsWindowsUnlessChosen()
    {
        var history = new UndoHistory();
        var chosen = Done(history, new ThermiConfig(), config => ThermiEdits.SetLanguage(config, "es"));

        var picked = new[] { CultureInfo.GetCultureInfo("es-UY"), CultureInfo.GetCultureInfo("fr-FR"), English }.Select(windows => Texts.Pick(null, windows)).Append(Texts.Pick(chosen.Language, English));

        Assert.Equal(["es", "en", "en", "es"], picked);
        Assert.Equal(("RED", "Salir"), (Texts.Of("es").Of(MetricLabel.Net), Texts.For(chosen.Language, English).Of(UiText.Exit)));
        Assert.Equal(("NET", "Exit"), (Texts.English.Of(MetricLabel.Net), Texts.Of("fr").Of(UiText.Exit)));
        Assert.Equal("es", Texts.Pick("fr", CultureInfo.GetCultureInfo("es-ES")));
        Assert.Null(history.Undo(chosen)!.Config.Language);
        Assert.Equal(Refusal.UnknownLanguage, ThermiEdits.SetLanguage(chosen, "fr").Refused);
    }

    [Fact]
    public void NumbersFollowTheRegionalFormatWhateverTheLanguage()
    {
        var overlay = new OverlayConfig();
        var readings = SensorMap.Read(Desktop, overlay);

        var englishOnSpanishFormat = OverlayLayout.Build(overlay, readings, CultureInfo.GetCultureInfo("es-ES"), Texts.English);
        var spanishOnEnglishFormat = OverlayLayout.Build(overlay, readings, English, Texts.Of("es"));

        Assert.Equal([(null, "1.087 RPM"), ("RAM", "27,2 GB")], new[] { englishOnSpanishFormat[1], englishOnSpanishFormat[^1] }.Select(item => (item.Label, item.Value)));
        Assert.Equal([(null, "1,087 RPM"), ("RAM", "27.2 GB")], new[] { spanishOnEnglishFormat[1], spanishOnEnglishFormat[^1] }.Select(item => (item.Label, item.Value)));
        Assert.EndsWith(" 1,5", Texts.English.Format(CultureInfo.GetCultureInfo("es-ES"), UiText.OverlayName, 1.5), StringComparison.Ordinal);
        Assert.Equal(Texts.Of("es").Of(Refusal.Invalid).Replace("{0}", "size", StringComparison.Ordinal), Texts.Of("es-UY").Format(English, Refusal.Invalid, "size"));
        Assert.Equal("Refusal.NoMeter", Texts.Key(Refusal.NoMeter));
    }
}
