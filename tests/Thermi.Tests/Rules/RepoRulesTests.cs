using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Thermi.Core.Settings;

namespace Thermi.Tests.Rules;

/// <summary>
/// Rules about the repository itself that no compiler checks. Each one is listed in AGENTS.md;
/// a rule that is only written down stops being followed.
/// </summary>
public sealed partial class RepoRulesTests
{
    private const int MaxLinesPerFile = 400;

    private static readonly string Root = TestFiles.Root;

    private static readonly string[] SkippedFolders =
        ["bin", "obj", ".git", ".vs", "TestResults", "artifacts", "worktrees", "node_modules", "dist", "test-results", "playwright-report"];

    private static readonly string[] CodeExtensions = [".cs", ".ps1", ".ts", ".tsx", ".js", ".mjs"];

    private static readonly string[] TextExtensions =
        [".cs", ".ps1", ".ts", ".tsx", ".html", ".md", ".json", ".yml", ".props", ".csproj", ".xml", ".resx", ".txt", ".globalconfig"];

    // A line past what a side-by-side diff shows is read by scrolling, and a long one hides a second thought in its end.
    [Fact]
    public void CodeLinesStayNarrow()
    {
        const int MaxLineLength = 160;
        var tooWide = RepoFiles(CodeExtensions)
            .Where(path => !path.EndsWith(".g.ts", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, index))
                .Where(each => each.line.Length > MaxLineLength)
                .Select(each => $"{Relative(path)}:{each.index + 1} is {each.line.Length} characters wide, more than {MaxLineLength}"));

        AssertNone(tooWide);
    }

    [Fact]
    public void CodeFilesStayShort()
    {
        // The page's .g.ts files are written in the build from Core's records, one line per field, so the files those
        // records live in, which this counts, bound them.
        var tooLong = RepoFiles(CodeExtensions)
            .Where(path => !path.EndsWith(".g.ts", StringComparison.Ordinal))
            .Select(path => (path, lines: File.ReadLines(path).Count()))
            .Where(file => file.lines > MaxLinesPerFile)
            .Select(file => $"{Relative(file.path)} has {file.lines} lines");

        AssertNone(tooLong);
    }

    // A folder of loose files hides its topics: past a dozen, a reader scans the list instead of finding the folder.
    // The fullest hold ten, so a topic can grow a little before it splits. Generated code is in obj, never counted;
    // a test project's Acceptance folder follows the spec's steps, not the code's topics.
    [Fact]
    public void FoldersHoldAtMostTwelveCodeFiles()
    {
        const int MaxCodeFilesPerFolder = 12;
        string[] uncounted = ["Acceptance", "Recordings"];
        var crowded = RepoFiles([".cs"])
            .Where(path => !(Relative(path).StartsWith("tests", StringComparison.Ordinal)
                && Relative(path).Split(Path.DirectorySeparatorChar).Any(part => uncounted.Contains(part, StringComparer.Ordinal))))
            .GroupBy(path => Relative(Path.GetDirectoryName(path)!), StringComparer.OrdinalIgnoreCase)
            .Where(folder => folder.Count() > MaxCodeFilesPerFolder)
            .Select(folder => $"{folder.Key} holds {folder.Count()} code files: move them into folders by topic");

        AssertNone(crowded);
    }

    // What a regex can decide about comments. The comment-trimmer agent judges the rest.
    [Fact]
    public void ProductionCommentsHoldNoHistoryAndNoLongBlocks()
    {
        var findings = RepoFiles([".cs", ".js"])
            .Where(path => Relative(path).StartsWith("src", StringComparison.Ordinal))
            .SelectMany(path => CommentFindings(Relative(path), File.ReadAllLines(path)));

        AssertNone(findings);
    }

    [Fact]
    public void EveryProjectHasAReadme()
    {
        var projects = ProjectFolders().ToList();
        Assert.NotEmpty(projects);

        AssertNone(projects
            .Where(folder => !SaysSomething(Path.Combine(folder, "README.md")))
            .Select(Relative));
    }

    [Fact]
    public void CoreDependsOnNothing()
    {
        var project = File.ReadAllText(Path.Combine(Root, "src", "Thermi.Core", "Thermi.Core.csproj"));

        Assert.DoesNotContain("PackageReference", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
    }

    // The window is a web page in WebView2 (docs/adr/0007-settings-window-in-webview2-and-react.md): no project builds on
    // WPF, so no process of thermi's can load it.
    [Fact]
    public void NothingUsesWpf()
    {
        var code = RepoFiles([".cs", ".xaml"])
            .SelectMany(path => Path.GetExtension(path) == ".xaml" ? [$"{Relative(path)} is WPF markup"] : File.ReadLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => Wpf().IsMatch(entry.line))
                .Select(entry => $"{Relative(path)}:{entry.number} uses WPF"));
        var projects = RepoFiles([".csproj", ".props", ".targets"])
            .Where(path => File.ReadAllText(path).Contains("<UseWPF>", StringComparison.Ordinal))
            .Select(path => $"{Relative(path)} turns WPF on");

        AssertNone(code.Concat(projects));
    }

    // A WPF namespace; Windows Forms shares the prefix but is not WPF.
    [GeneratedRegex(@"\bSystem\.Windows\b(?!\.Forms\b)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Wpf();

    [Fact]
    public void DocsOnlyCitePathsThatExist()
    {
        var broken = RepoFiles([".md"])
            .SelectMany(doc => CitedPath().Matches(File.ReadAllText(doc))
                .Select(match => match.Groups["path"].Value)
                .Where(cited => !Exists(Root, cited) && !Exists(Path.GetDirectoryName(doc)!, cited))
                .Select(cited => $"{Relative(doc)} cites {cited}"));
        var projects = RepoFiles([".csproj", ".wixproj"]).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folders = ProjectFolders().ToList();
        var stale = RepoFiles([.. CodeExtensions, ".csproj", ".props", ".targets", ".wxs"])
            .SelectMany(code => CodePath().Matches(File.ReadAllText(code))
                .Select(match => match.Groups["path"].Value.TrimEnd('.'))
                .Where(cited => cited.EndsWith("proj", StringComparison.Ordinal)
                    ? !projects.Contains(cited)
                    : !Ancestors(Path.GetDirectoryName(code)!).Concat(folders).Any(folder => Exists(folder, cited)))
                .Select(cited => $"{Relative(code)} cites {cited}"));

        AssertNone(broken.Concat(stale));
    }

    private static IEnumerable<string> Ancestors(string folder)
    {
        for (var at = folder; at.Length >= Root.Length; at = Path.GetDirectoryName(at)!)
        {
            yield return at;
        }
    }

    // A path from the repo's root, or, in a project's README, from the project's folder.
    private static bool Exists(string folder, string cited) =>
        File.Exists(Path.Combine(folder, cited)) || Directory.Exists(Path.Combine(folder, cited));

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

    // A public repo's recordings and snapshots are read by anyone, so they name no PC, person or network: an adapter's id is
    // its interface's GUID, so a recording's adapters carry made-up ones, 00000000-0000-4000-8000-000000000001 on. A Linux
    // tree's files are the kernel's, of any name, and all are read, and so are their paths, which name a device as the
    // kernel does, a colon kept as %3A. A folder that moved fails here too, or this would pass without reading a thing.
    [Fact]
    public void RecordedDataNamesNoMachinePersonOrNetwork()
    {
        string[] watched = [Path.Combine(Root, "tests"), Path.Combine(Root, "src", "Thermi.Settings.Web", "tests")];
        var trees = Path.Combine("tests", "Thermi.Tests", "Recordings", "Trees");
        var data = watched.SelectMany(FilesUnder)
            .Where(path => (Path.GetExtension(path) is ".json" or ".jsonl" && Path.GetFileName(path) != "packages.lock.json")
                || Relative(path).StartsWith(trees, StringComparison.Ordinal))
            .ToList();
        Assert.Contains(data, path => Relative(path).StartsWith(Path.Combine("tests", "Thermi.Tests", "Recordings"), StringComparison.Ordinal));
        Assert.Contains(data, path => Relative(path).StartsWith(trees, StringComparison.Ordinal) && Path.GetExtension(path).Length == 0);
        Assert.Contains(data, path => Path.GetFileName(path) == "edits.json");

        var paths = data.Where(path => Relative(path).StartsWith(trees, StringComparison.Ordinal))
            .SelectMany(path => PersonalData().Matches(Uri.UnescapeDataString(Relative(path)))
                .Select(match => $"{Relative(path)} is named for {match.Value}: replace it with a made-up one"));
        AssertNone(data.SelectMany(path => PersonalData().Matches(File.ReadAllText(path))
            .Select(match => $"{Relative(path)} holds {match.Value}: replace it with a made-up one")).Concat(paths));
    }

    // Real GUIDs, MACs, Linux's adapters named by theirs, IPv4s (loopback and documentation ranges aside), Windows' and
    // Linux's user folders, default PC names and identity fields.
    [GeneratedRegex(
        @"(?!00000000-0000-4000-8000-)[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}(?![0-9A-F])|\b(?:[0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b"
        + @"|\b(?:enx|wlx)[0-9A-F]{12}\b"
        + @"|\b(?!127\.|192\.0\.2\.|198\.51\.100\.|203\.0\.113\.)(?:\d{1,3}\.){3}\d{1,3}\b|[A-Z]:(?:\\|/)+Users(?:\\|/)+(?!Public\b)\w|/home/\w"
        + @"|\b(?:DESKTOP|LAPTOP)-[A-Z0-9]{7}\b|""(?:machine|computer|host|user)_?name""|""(?:ssid|serial|serialNumber|serial_number)""",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex PersonalData();

    // A silenced warning without a reason is how hundreds of suppressions pile up.
    [Fact]
    public void EverySuppressionSaysWhy()
    {
        const string Pragma = "#pragma" + " warning disable";
        var silent = RepoFiles([".cs"])
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => (entry.line.Contains(Pragma, StringComparison.Ordinal) && !entry.line.Contains("//", StringComparison.Ordinal))
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

    // A backticked repo path with a folder and an extension, like `src/Thermi.Core/Sensors/Reading.cs`.
    [GeneratedRegex(@"`(?<path>(?:src|tests|docs|scripts|\.github|\.claude)/[\w./-]+)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CitedPath();

    // A repo path or a project file that code or its comments name, like src/Thermi.Windows/Program.cs or Thermi.Core.csproj,
    // from the root, a folder above the file or a project's folder; not one a template string goes on to build.
    [GeneratedRegex(
        @"(?<![\w./\\-])(?<path>(?:src|tests|docs|scripts)/[\w.-]+(?:/[\w.-]+)*|[A-Z][\w.]*\.(?:csproj|wixproj))(?![\w$*{<./-])",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodePath();

    // A drive path other than the standard install folders, like a user profile or a work folder.
    [GeneratedRegex(@"\b[A-Za-z]:[\\/](?!Program Files|Windows[\\/])", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AbsoluteLocalPath();

    // Process history in a comment: issue or spec numbers, or words that announce it will change.
    [GeneratedRegex(
        @"//.*(\b(SPEC|BUG|DEF|ISSUE)-?\d+|#\d+\b|\(R\d+\)|\bfor now\b|\btemporar(y|ily)\b|\bwill be (migrated|removed|replaced)\b)",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
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

            var trimmed = lines[index].TrimStart();
            var isBodyComment = trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("///", StringComparison.Ordinal);
            block = isBodyComment ? block + 1 : 0;
            if (block == MaxCommentBlock + 1)
            {
                yield return $"{file}:{index + 1} ends more than {MaxCommentBlock} comment lines in a row: a contract goes in /// docs, history goes away";
            }
        }
    }

    // A version rolled back to reads a file of its own version as its own. So the settings change shape only with a new
    // version and its migration, or with the reason an earlier version reads them as they are, as what it keeps unknown.
    [Fact]
    public void TheSettingsChangeShapeOnlyWithTheirVersionOrAReason()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(ThermiConfig)).ToJsonString();
        var shape = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(schema)))[..16];
        var file = Path.Combine(Root, "tests", "Thermi.Tests", "Configs", "shapes.json");
        var shapes = JsonNode.Parse(File.ReadAllText(file))!["shapes"]!.AsArray()
            .Select(each => (Version: (int)each!["version"]!, Shape: (string)each["shape"]!, Why: (string?)each["why"] ?? "")).ToList();

        AssertNone(shapes.Zip(shapes.Skip(1))
            .Where(pair => pair.Second.Version < pair.First.Version || (pair.Second.Version == pair.First.Version && pair.Second.Why.Trim().Length == 0))
            .Select(pair => $"{Relative(file)}: shape {pair.Second.Shape} keeps or lowers version {pair.First.Version} without saying why")
            .Concat(shapes[^1] == (ThermiConfig.CurrentVersion, shape, shapes[^1].Why) ? [] : [
                $"The settings' shape is {shape} at version {ThermiConfig.CurrentVersion}: add it to {Relative(file)}, with the reason a version"
                + " that knows only the last shape reads it as it is when the version stays",
            ]));
    }

    // Assert.Empty truncates long messages; a broken rule must say exactly where.
    private static void AssertNone(IEnumerable<string> violations)
    {
        var all = violations.ToList();
        Assert.True(all.Count == 0, string.Join(Environment.NewLine, all));
    }

    private static IEnumerable<string> RepoFiles(string[] extensions) =>
        FilesUnder(Root).Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    // Skipped folders are never entered: node_modules alone holds thousands of files, and a walk through bin holds open
    // the folders that the Windows tests, run beside these, are moving there.
    private static IEnumerable<string> FilesUnder(string folder) =>
        Directory.EnumerateFiles(folder).Concat(Directory.EnumerateDirectories(folder)
            .Where(child => !SkippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            .SelectMany(FilesUnder));

    private static string Relative(string path) => Path.GetRelativePath(Root, path);

    private static bool SaysSomething(string readme) =>
        File.Exists(readme) && File.ReadLines(readme).Any(line => line.Trim().Length > 0 && !line.StartsWith('#'));
}
