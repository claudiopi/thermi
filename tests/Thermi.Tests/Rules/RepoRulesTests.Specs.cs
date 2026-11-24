using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

/// <summary>The specs: each a folder whose requirements all have an acceptance step, each checked by a real test.</summary>
public sealed partial class RepoRulesTests
{
    private static readonly string WebApp = Path.Combine("src", "Thermi.Settings.Web") + Path.DirectorySeparatorChar;

    [Fact]
    public void SpecsAreContractsThatHold()
    {
        var tests = RepoFiles([".cs"])
            .Where(path => Relative(path).StartsWith("tests", StringComparison.Ordinal))
            .SelectMany(path => TestMethod().Matches(File.ReadAllText(path)).Select(match => match.Groups["name"].Value))
            .Concat(PlaywrightTitles())
            .ToHashSet(StringComparer.Ordinal);
        var specs = Directory.EnumerateDirectories(Path.Combine(Root, "docs", "specs")).ToList();

        Assert.NotEmpty(specs);
        AssertNone(specs.SelectMany(spec => SpecFindings(spec, tests)));
    }

    private static IEnumerable<string> SpecFindings(string folder, HashSet<string> tests)
    {
        var name = Relative(folder);
        string[] files = ["spec.md", "tasks.md", "acceptance.json"];
        var missing = files.Where(file => !File.Exists(Path.Combine(folder, file))).Select(file => $"{name} has no {file}").ToList();
        if (missing.Count > 0)
        {
            return missing;
        }

        string[] statuses = ["draft", "approved", "done", "dropped"];
        var spec = File.ReadAllLines(Path.Combine(folder, "spec.md"));
        var status = spec.FirstOrDefault(line => line.StartsWith("**Status**: ", StringComparison.Ordinal))?["**Status**: ".Length..];
        var requirements = spec.Select(line => Requirement().Match(line)).Where(match => match.Success).Select(match => match.Groups["id"].Value).ToList();
        var steps = Steps(Path.Combine(folder, "acceptance.json"));
        var covered = steps.SelectMany(step => step.Covers).ToList();
        bool Checked((string Id, List<string> Covers, string Check, bool Passes) step)
        {
            return step.Check == "by hand" || (status is "draft" or "dropped" ? TestName().IsMatch(step.Check) : tests.Contains(step.Check));
        }

        return new[] { statuses.Contains(status, StringComparer.Ordinal) ? null : $"{name} has no known status (got '{status}')" }
            .Concat(requirements.Except(covered, StringComparer.Ordinal).Select(id => $"{name} {id} is covered by no acceptance step"))
            .Concat(covered.Except(requirements, StringComparer.Ordinal).Select(id => $"{name} covers {id}, which spec.md does not have"))
            .Concat(steps.Where(step => !Checked(step))
                .Select(step => $"{name} {step.Id} checks with '{step.Check}', which is neither a test nor 'by hand'"))
            .Concat(status == "done" ? steps.Where(step => !step.Passes).Select(step => $"{name} is done but {step.Id} does not pass") : [])
            .OfType<string>();
    }

    private static List<(string Id, List<string> Covers, string Check, bool Passes)> Steps(string file)
    {
        using var acceptance = JsonDocument.Parse(File.ReadAllText(file));
        return [.. acceptance.RootElement.GetProperty("steps").EnumerateArray().Select(step => (
            step.GetProperty("id").GetString() ?? "",
            step.GetProperty("covers").EnumerateArray().Select(cover => cover.GetString() ?? "").ToList(),
            step.GetProperty("check").GetString() ?? "",
            step.GetProperty("passes").GetBoolean()))];
    }

    // The settings window's tests are Playwright's, titled as one PascalCase word so a step names them as it names a .NET test.
    private static IEnumerable<string> PlaywrightTitles() =>
        RepoFiles([".ts", ".tsx"])
            .Where(path => Relative(path).StartsWith(WebApp, StringComparison.Ordinal)
                && (path.EndsWith(".spec.ts", StringComparison.Ordinal) || path.EndsWith(".spec.tsx", StringComparison.Ordinal)))
            .SelectMany(path => PlaywrightTest().Matches(File.ReadAllText(path)).Select(match => match.Groups["name"].Value));

    // A Playwright test, like "test('TheChartShows', ...", also when it waits behind skip, fail or its not-yet-written
    // marker, or a table of cases under one describe of that name.
    [GeneratedRegex(
        @"\btest(\.(skip|fixme|fail|describe))?\(\s*(?<quote>['""`])(?<name>[A-Z]\w+)\k<quote>\s*,",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaywrightTest();

    // A draft names the tests it will need; they exist once it is approved, before any code.
    [GeneratedRegex(@"^[A-Z]\w+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestName();

    // A requirement line of a spec, like "- R3. Each meter turns yellow ...".
    [GeneratedRegex(@"^- (?<id>R\d+)\. ", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Requirement();

    // A test method: a line with [Fact] or [Theory], then the lines of other attributes, which a long row wraps over
    // several, then the method. Each line is passed once, so many rows cost no backtracking.
    [GeneratedRegex(
        @"\[(Fact|Theory)\b[^\n]*\n((?!\s*public\s)[^\n]*\n)*?\s*public\s+(async\s+)?(void|Task)\s+(?<name>\w+)\s*\(",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestMethod();
}
