using System.Text.Json;
using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

/// <summary>
/// The specs: each a folder whose requirements all have an acceptance step, each checked by a real test, or a release
/// check or hardware report that covers what no test can.
/// </summary>
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
        AssertNone(specs.SelectMany(spec => SpecFindings(spec, tests)), "docs/specs/README.md gives each file's shape");
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
        var (byPerson, unknownChecks) = PersonChecks(name, spec);
        bool Checked((string Id, List<string> Covers, string Check, bool Passes, string Description) step)
        {
            return status is "draft" or "dropped" ? TestName().IsMatch(step.Check) : tests.Contains(step.Check);
        }

        return new[] { statuses.Contains(status, StringComparer.Ordinal) ? null : $"{name} has no known status (got '{status}')" }
            .Concat(steps.CountBy(step => step.Id, StringComparer.Ordinal).Where(id => id.Value > 1)
                .Select(id => $"{name} has the acceptance step {id.Key} {id.Value} times"))
            .Concat(requirements.Except(covered.Concat(byPerson), StringComparer.Ordinal)
                .Select(id => $"{name} {id} is covered by no acceptance step, release check or hardware report"))
            .Concat(covered.Except(requirements, StringComparer.Ordinal).Select(id => $"{name} covers {id}, which spec.md does not have"))
            .Concat(unknownChecks)
            .Concat(steps.Where(step => !Checked(step)).Select(step => $"{name} {step.Id} checks with '{step.Check}', which is not a test"))
            .Concat(status == "done" ? steps.Where(step => !step.Passes).Select(step => $"{name} is done but {step.Id} does not pass") : [])
            .Concat(steps.Select(step => (step.Id, Rows: TableRows(step.Description)))
                .Where(step => step.Rows > MaxKindsPerStep)
                .Select(step => $"{name} {step.Id} lists {step.Rows} cases of its table, more than {MaxKindsPerStep}: a step names the kinds of " +
                    "case its test proves, and the rows are the test's"))
            .OfType<string>();
    }

    // What a person checks instead of a test, a cost on the installed build or what a screen reader says, never holds a spec
    // back: the requirement names on the line under it the check of docs/RELEASES.md, or the hardware report's item, that
    // covers it, and the name has to be there.
    private static (List<string> Covered, List<string> Unknown) PersonChecks(string name, string[] spec)
    {
        var known = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["Release check"] = Names(Path.Combine(Root, "docs", "RELEASES.md"), ReleaseCheck()),
            ["Hardware report"] = Names(Path.Combine(Root, ".github", "ISSUE_TEMPLATE", "hardware-report.yml"), HardwareItem()),
        };
        var (covered, unknown) = (new List<string>(), new List<string>());
        string? requirement = null;
        foreach (var line in spec)
        {
            var check = PersonCheck().Match(line);
            if (!check.Success)
            {
                requirement = Requirement().Match(line) is { Success: true } match ? match.Groups["id"].Value : null;
                continue;
            }

            var (kind, named) = (check.Groups["kind"].Value, check.Groups["name"].Value);
            if (requirement is not null && known[kind].Contains(named))
            {
                covered.Add(requirement);
            }
            else
            {
                var why = requirement is null ? "under no requirement" : "which does not exist";
                unknown.Add($"{name} names the {kind.ToLowerInvariant()} '{named}', {why}");
            }
        }

        return (covered, unknown);
    }

    private static HashSet<string> Names(string file, Regex name) =>
        File.ReadLines(file).Select(line => name.Match(line)).Where(match => match.Success).Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    // A table's cases, as "a table of edits (new, a move, a drop)" lists them: a step names their kinds, so a row its test no
    // longer needs goes without changing what a person approved.
    private const int MaxKindsPerStep = 5;

    private static int TableRows(string description)
    {
        var table = StepTable().Match(description);
        return table.Success ? table.Groups["cases"].Value.Count(letter => letter is ',' or ';') + 1 : 0;
    }

    private static List<(string Id, List<string> Covers, string Check, bool Passes, string Description)> Steps(string file)
    {
        using var acceptance = JsonDocument.Parse(File.ReadAllText(file));
        return [.. acceptance.RootElement.GetProperty("steps").EnumerateArray().Select(step => (
            step.GetProperty("id").GetString() ?? "",
            step.GetProperty("covers").EnumerateArray().Select(cover => cover.GetString() ?? "").ToList(),
            step.GetProperty("check").GetString() ?? "",
            step.GetProperty("passes").GetBoolean(),
            step.GetProperty("description").GetString() ?? ""))];
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

    [GeneratedRegex(@"\btables? of [^()]*?\((?<cases>[^()]*)\)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StepTable();

    // A draft names the tests it will need; they exist once it is approved, before any code.
    [GeneratedRegex(@"^[A-Z]\w+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestName();

    // A requirement line of a spec, like "- R3. Each meter turns yellow ...".
    [GeneratedRegex(@"^- (?<id>R\d+)\. ", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Requirement();

    // The line under a requirement, like "  Release check: Costs at rest.".
    [GeneratedRegex(@"^  (?<kind>Release check|Hardware report): (?<name>.+)\.$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PersonCheck();

    // A check of docs/RELEASES.md, the first cell of its table's row, like "| **Costs at rest** | ...".
    [GeneratedRegex(@"^\| \*\*(?<name>[^*]+)\*\* \|", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReleaseCheck();

    // An item of the hardware report, an option of what was tried, like "- label: "Arm64: the machine MSI installs ..."".
    [GeneratedRegex(@"^\s*- label: ""?(?<name>[^:""]+):", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HardwareItem();

    // A test method: a line with [Fact] or [Theory], then the lines of other attributes, which a long row wraps over
    // several, then the method. Each line is passed once, so many rows cost no backtracking.
    [GeneratedRegex(
        @"\[(Fact|Theory)\b[^\n]*\n((?!\s*public\s)[^\n]*\n)*?\s*public\s+(async\s+)?(void|Task)\s+(?<name>\w+)\s*\(",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestMethod();
}
