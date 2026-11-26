using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    private static readonly string[] CodeFileExtensions = [".cs", ".ts", ".tsx"];

    // A README that maps a project's folders goes stale the day a folder is added or renamed: its table must name every
    // folder that holds code, and no folder that is gone.
    [Fact]
    public void EveryProjectReadmeMapsItsFolders()
    {
        var findings = ProjectFolders().SelectMany(project =>
        {
            var readme = Path.Combine(project, "README.md");
            var mapped = FolderTable(File.Exists(readme) ? File.ReadAllLines(readme) : []);
            var withCode = FilesUnder(project)
                .Where(path => CodeFileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(project, Path.GetDirectoryName(path)!).Replace('\\', '/'))
                .Where(folder => folder != ".")
                .Distinct(StringComparer.Ordinal);
            var missing = withCode.Where(folder => !mapped.Contains(folder))
                .Select(folder => $"{Relative(readme)} has no row for its folder `{folder}` in its Folder table");
            var gone = mapped.Where(folder => !Directory.Exists(Path.Combine(project, folder)))
                .Select(folder => $"{Relative(readme)} maps `{folder}`, which does not exist");
            return missing.Concat(gone);
        });

        AssertNone(findings);
    }

    // The first cells of the table whose header starts with "Folder", each a backticked folder.
    private static HashSet<string> FolderTable(IEnumerable<string> lines)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var inTable = false;
        foreach (var line in lines.Select(each => each.Trim()))
        {
            if (!line.StartsWith('|'))
            {
                inTable = false;
                continue;
            }

            var first = line.Split('|', StringSplitOptions.TrimEntries)[1];
            if (first.StartsWith("Folder", StringComparison.Ordinal))
            {
                inTable = true;
            }
            else if (inTable && FolderCell().Match(first) is { Success: true } cell)
            {
                folders.Add(cell.Groups["folder"].Value);
            }
        }

        return folders;
    }

    [GeneratedRegex(@"^`(?<folder>[\w./-]+)`$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FolderCell();

    // A count or a score quoted in the README is stale by the next change, and the ratchet's own raise may touch nothing
    // else: its badges and each CI run's summary show the tests' counts, the coverage and the scores.
    [Fact]
    public void TheReadmeQuotesNoMeasuredNumber()
    {
        var quoted = File.ReadAllLines(Path.Combine(Root, "README.md"))
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(each => MeasuredNumber().IsMatch(each.Line))
            .Select(each => $"README.md:{each.Number} quotes a count or a score, which goes stale: link the CI's summary or a badge");

        AssertNone(quoted);
    }

    [GeneratedRegex(@"\d\s*%|\d[\d,]*\s+(\.NET\s+|page\s+)?cases\b", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MeasuredNumber();
}
