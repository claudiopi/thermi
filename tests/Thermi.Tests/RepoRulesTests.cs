using System.Text.RegularExpressions;

namespace Thermi.Tests;

/// <summary>
/// Rules about the repository itself that no compiler checks. Each one is listed in AGENTS.md;
/// a rule that is only written down stops being followed.
/// </summary>
public sealed partial class RepoRulesTests
{
    private const int MaxLinesPerFile = 400;

    private static readonly string Root = FindRoot();

    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", ".vs", "TestResults", "artifacts", "worktrees"];

    private static readonly string[] CodeExtensions = [".cs", ".xaml", ".ps1"];

    private static readonly string[] TextExtensions = [".cs", ".xaml", ".ps1", ".md", ".json", ".yml", ".props", ".csproj", ".xml", ".txt", ".globalconfig"];

    [Fact]
    public void CodeFilesStayShort()
    {
        var tooLong = RepoFiles(CodeExtensions)
            .Select(path => (path, lines: File.ReadLines(path).Count()))
            .Where(file => file.lines > MaxLinesPerFile)
            .Select(file => $"{Relative(file.path)} has {file.lines} lines");

        AssertNone(tooLong);
    }

    // What a regex can decide about comments. The comment-trimmer agent judges the rest.
    [Fact]
    public void ProductionCommentsHoldNoHistoryAndNoLongBlocks()
    {
        var findings = RepoFiles([".cs"])
            .Where(path => Relative(path).StartsWith("src", StringComparison.Ordinal))
            .SelectMany(path => CommentFindings(Relative(path), File.ReadAllLines(path)));

        AssertNone(findings);
    }

    [Fact]
    public void CoreDependsOnNothing()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "Thermi.Core", "Thermi.Core.csproj"));

        Assert.DoesNotContain("PackageReference", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
    }

    [Fact]
    public void DocsOnlyCitePathsThatExist()
    {
        var broken = RepoFiles([".md"])
            .SelectMany(doc => CitedPath().Matches(File.ReadAllText(doc))
                .Select(match => match.Groups["path"].Value)
                .Where(cited => !File.Exists(Path.Combine(Root, cited)) && !Directory.Exists(Path.Combine(Root, cited)))
                .Select(cited => $"{Relative(doc)} cites {cited}"));

        AssertNone(broken);
    }

    [Fact]
    public void NoAbsoluteLocalPaths()
    {
        var leaks = RepoFiles(TextExtensions)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => AbsoluteLocalPath().IsMatch(entry.line))
                .Select(entry => $"{Relative(path)}:{entry.number}"));

        AssertNone(leaks);
    }

    // A silenced warning without a reason is how hundreds of suppressions pile up.
    [Fact]
    public void EverySuppressionSaysWhy()
    {
        var silent = RepoFiles([".cs"])
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => (entry.line.Contains("#pragma warning disable", StringComparison.Ordinal) && !entry.line.Contains("//", StringComparison.Ordinal))
                    || (entry.line.Contains("SuppressMessage(", StringComparison.Ordinal) && !entry.line.Contains("Justification", StringComparison.Ordinal)))
                .Select(entry => $"{Relative(path)}:{entry.number} suppresses a warning without saying why"));

        AssertNone(silent);
    }

    // Rules are turned off only in Thermi.globalconfig, each with its reason on the line above.
    // NuGet warnings (NU1xxx, such as a vulnerable package) are fixed by updating, never hidden.
    [Fact]
    public void BuildSuppressionsSayWhyAndNeverHideNuGet()
    {
        var buildFiles = RepoFiles([".csproj", ".props", ".targets"]).ToList();
        var noWarn = buildFiles
            .Where(file => File.ReadAllText(file).Contains("<NoWarn>", StringComparison.Ordinal))
            .Select(file => $"{Relative(file)} uses NoWarn: turn the rule off in Thermi.globalconfig, with the reason");
        var nuget = buildFiles.Append(Path.Combine(Root, "Thermi.globalconfig"))
            .Where(file => NuGetWarning().IsMatch(File.ReadAllText(file)))
            .Select(file => $"{Relative(file)} silences a NuGet warning: update the package instead");
        var config = File.ReadAllLines(Path.Combine(Root, "Thermi.globalconfig"));
        var unexplained = config
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.EndsWith("= none", StringComparison.Ordinal) && (entry.index == 0 || !config[entry.index - 1].StartsWith('#')))
            .Select(entry => $"Thermi.globalconfig:{entry.index + 1} turns a rule off without a reason on the line above");

        AssertNone(noWarn.Concat(nuget).Concat(unexplained));
    }

    // A subagent without a model runs on its caller's model, which is usually the most expensive one.
    [Fact]
    public void AgentsPinTheirModel()
    {
        var unpinned = Directory.EnumerateFiles(Path.Combine(Root, ".claude", "agents"), "*.md")
            .Where(file => !File.ReadLines(file).Any(line => line.StartsWith("model:", StringComparison.Ordinal)))
            .Select(file => $"{Relative(file)} has no model: line");

        AssertNone(unpinned);
    }

    [GeneratedRegex(@"\bNU\d{4}\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NuGetWarning();

    // A path out of the repo breaks on every other clone, with errors far from the cause.
    [Fact]
    public void BuildFilesOnlyPointInsideTheRepo()
    {
        var escapes = RepoFiles([".csproj", ".props", ".targets", ".slnx"])
            .SelectMany(file => MsBuildPath().Matches(File.ReadAllText(file))
                .Select(match => match.Groups["path"].Value)
                .Where(path => !Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, path)).StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                .Select(path => $"{Relative(file)} points outside the repo: {path}"));

        AssertNone(escapes);
    }

    // A relative path in a build file that climbs at least one folder, like "..\..\src\x.csproj".
    [GeneratedRegex(@"""(?<path>(\.\.[\\/])+[^""]*)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MsBuildPath();

    // A backticked repo path with a folder and an extension, like `src/Thermi.Core/Reading.cs`.
    [GeneratedRegex(@"`(?<path>(?:src|tests|docs|scripts|\.github|\.claude)/[\w./-]+)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CitedPath();

    // A drive path other than the standard install folders, like a user profile or a work folder.
    [GeneratedRegex(@"\b[A-Za-z]:[\\/](?!Program Files|Windows[\\/])", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AbsoluteLocalPath();

    // Process history in a comment: issue or spec numbers, or words that announce it will change.
    [GeneratedRegex(@"//.*(\b(SPEC|BUG|DEF|ISSUE)-?\d+|#\d+\b|\(R\d+\)|\bfor now\b|\btemporar(y|ily)\b|\bwill be (migrated|removed|replaced)\b)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HistoryComment();

    private static IEnumerable<string> CommentFindings(string file, string[] lines)
    {
        const int MaxCommentBlock = 5;
        var block = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (HistoryComment().IsMatch(lines[index]))
            {
                yield return $"{file}:{index + 1} has process history in a comment: it belongs in git or an issue";
            }

            var isBodyComment = lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal) && !lines[index].TrimStart().StartsWith("///", StringComparison.Ordinal);
            block = isBodyComment ? block + 1 : 0;
            if (block == MaxCommentBlock + 1)
            {
                yield return $"{file}:{index + 1} ends more than {MaxCommentBlock} comment lines in a row: a contract goes in /// docs, history goes away";
            }
        }
    }

    // Assert.Empty truncates long messages; a broken rule must say exactly where.
    private static void AssertNone(IEnumerable<string> violations)
    {
        var all = violations.ToList();
        Assert.True(all.Count == 0, string.Join(Environment.NewLine, all));
    }

    private static IEnumerable<string> RepoFiles(string[] extensions) =>
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !Relative(path).Split(Path.DirectorySeparatorChar).Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase)));

    private static string Relative(string path) => Path.GetRelativePath(Root, path);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "thermi.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("thermi.slnx not found above the test output folder.");
    }
}
