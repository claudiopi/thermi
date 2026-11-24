using Thermi.Core.Search;
using Thermi.Core.Sensors;
using Thermi.Core.Translations;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Every search of the window finds by words in any order, without accents or case, puts the best first and marks on each
/// row what matched, so the row says why it is there.
/// </summary>
public sealed class SearchTests
{
    private static readonly string CpuTemperature = Texts.English.Of(MetricName.CpuTemperature);

    /// <summary>
    /// What is typed, and the first rows the catalog of the recorded desktop lists for it, each as its name with what matched
    /// in brackets, then the alias that explains it after "=", then what matched of its second line after "|".
    /// </summary>
    public static TheoryData<string, string[]> Searches => new()
    {
        // The title that starts with it first; the sensor thermi reads as CPU temperature by that name, after the titles.
        { "cpu temperature", ["[CPU] | Temperature", $"Core (Tctl/Tdie) = {Marked(CpuTemperature, "cpu temperature")} | Temperature"] },
        // Any order, any case, any accent.
        { "TEMPÉRATURE cpu", ["[CPU] | Temperature", $"Core (Tctl/Tdie) = {Marked(CpuTemperature, "cpu temperature")} | Temperature"] },
        { "package gpu", ["[GPU] [Package]"] },
        { "ccd1", ["[CCD1] (Tdie)"] },
        // One letter wrong is forgiven in a long word once nothing matches as typed, never in a short one.
        { "tempreature", ["System | Temperature"] },
        { "tamp", [] },
    };

    [Theory]
    [MemberData(nameof(Searches))]
    public void TheSearchPutsTheBestFirstAndMarksWhy(string search, string[] first)
    {
        var devices = SensorCatalog.Filter(SensorCatalog.Build(TestRecordings.Read(TestRecordings.Admin)), search, []);

        var rows = devices.SelectMany(device => device.Sensors).Take(first.Length).Select(Shown);

        Assert.Equal(first, rows);
    }

    private static string Shown(CatalogSensor row)
    {
        var found = row.Found ?? throw new InvalidOperationException($"{row.Id} was listed without what found it.");
        var alias = found.Alias is { } some ? $" = {Brackets(some, found.AliasMarks)}" : "";
        var line = found.Detail.Count > 0 ? $" | {string.Join(" ", found.Detail.Select(mark => row.Line[mark.From..mark.To]))}" : "";
        return $"{Brackets(row.Name.Shown, found.Title)}{alias}{line}";
    }

    private static string Marked(string text, string search) => Brackets(text, SearchIndex.MarksIn(text, search));

    private static string Brackets(string text, IReadOnlyList<Mark> marks)
    {
        var shown = text;
        foreach (var mark in marks.Reverse())
        {
            shown = $"{shown[..mark.From]}[{shown[mark.From..mark.To]}]{shown[mark.To..]}";
        }

        return shown;
    }
}
