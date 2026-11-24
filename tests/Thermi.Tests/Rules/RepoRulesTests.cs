using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Thermi.Core.Translations;

namespace Thermi.Tests.Rules;

/// <summary>
/// Rules about the repository itself that no compiler checks. Each one is listed in AGENTS.md;
/// a rule that is only written down stops being followed.
/// </summary>
public sealed partial class RepoRulesTests
{
    private const int MaxLinesPerFile = 400;

    private static readonly string Root = FindRoot();

    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", ".vs", "TestResults", "artifacts", "worktrees", "node_modules", "dist", "test-results", "playwright-report"];

    private static readonly string[] CodeExtensions = [".cs", ".ps1", ".ts", ".tsx"];

    private static readonly string[] TextExtensions = [".cs", ".ps1", ".ts", ".tsx", ".html", ".md", ".json", ".yml", ".props", ".csproj", ".xml", ".resx", ".txt", ".globalconfig"];

    private static readonly string[] DriverProducts = ["PawnIO", "WinRing0"];

    private static readonly string WebApp = Path.Combine("src", "Thermi.Settings.Web") + Path.DirectorySeparatorChar;

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
            .Where(path => !(Relative(path).StartsWith("tests", StringComparison.Ordinal) && Relative(path).Split(Path.DirectorySeparatorChar).Any(part => uncounted.Contains(part, StringComparer.Ordinal))))
            .GroupBy(path => Relative(Path.GetDirectoryName(path)!), StringComparer.OrdinalIgnoreCase)
            .Where(folder => folder.Count() > MaxCodeFilesPerFolder)
            .Select(folder => $"{folder.Key} holds {folder.Count()} code files: move them into folders by topic");

        AssertNone(crowded);
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
    public void EveryProjectHasAReadme()
    {
        var projects = RepoFiles([".csproj"]).Concat(RepoFiles([".json"]).Where(path => Path.GetFileName(path) == "package.json"))
            .Select(Path.GetDirectoryName).ToList();
        Assert.NotEmpty(projects);

        AssertNone(projects
            .Where(folder => !SaysSomething(Path.Combine(folder!, "README.md")))
            .Select(folder => Relative(folder!)));
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

    // CsWin32 generates every Win32 declaration from a NativeMethods.txt; one written by hand is a layout nobody checks.
    [Fact]
    public void NoWin32CallIsDeclaredByHand()
    {
        var declared = RepoFiles([".cs"])
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => HandImport().IsMatch(entry.line))
                .Select(entry => $"{Relative(path)}:{entry.number} declares a Win32 call by hand: list it in NativeMethods.txt"));

        AssertNone(declared);
    }

    // The attribute anywhere in a line: qualified, with its Attribute suffix, or after another one in the same brackets.
    [GeneratedRegex(@"\b(?:DllImport|LibraryImport)(?:Attribute)?\s*\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HandImport();

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
            .Concat(translations.SelectMany(file => TranslationFindings(Relative(file), english, ResxTexts(file)))));
    }

    private static IEnumerable<string> TranslationFindings(string file, Dictionary<string, string?> english, Dictionary<string, string?> translation) =>
        english.Keys.Except(translation.Keys, StringComparer.Ordinal).Select(key => $"{file} has no text for {key}")
            .Concat(translation.Keys.Except(english.Keys, StringComparer.Ordinal).Select(key => $"{file} has {key}, which English does not"))
            .Concat(TextFindings(file, translation))
            .Concat(translation
                .Where(pair => english.TryGetValue(pair.Key, out var text) && !Blanks(text ?? "").SetEquals(Blanks(pair.Value ?? "")))
                .Select(pair => $"{file} fills other blanks than English in {pair.Key}"));

    // A blank text shows nothing where a person needs words, and the driver's product is never named (docs/adr/0010-driver-and-service-from-the-window.md).
    private static IEnumerable<string> TextFindings(string file, Dictionary<string, string?> texts) =>
        texts.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => $"{file} has a blank text for {pair.Key}")
            .Concat(texts.Where(pair => DriverProducts.Any(product => pair.Value?.Contains(product, StringComparison.OrdinalIgnoreCase) == true))
                .Select(pair => $"{file} names the driver's product in {pair.Key}"));

    private static Dictionary<string, string?> ResxTexts(string file) =>
        XDocument.Load(file).Root!.Elements("data").ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value"), StringComparer.Ordinal);

    private static HashSet<string> Blanks(string text) => [.. Blank().Matches(text).Select(match => match.Value)];

    // A blank a text is filled with, like "{0}" or "{1:N0}".
    [GeneratedRegex(@"\{\d+[^}]*\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Blank();

    // A subagent without a model runs on its caller's model, which is usually the most expensive one.
    [Fact]
    public void AgentsPinTheirModel()
    {
        var unpinned = Directory.EnumerateFiles(Path.Combine(Root, ".claude", "agents"), "*.md")
            .Where(file => !File.ReadLines(file).Any(line => line.StartsWith("model:", StringComparison.Ordinal)))
            .Select(file => $"{Relative(file)} has no model: line");

        AssertNone(unpinned);
    }

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

        return new[] { statuses.Contains(status, StringComparer.Ordinal) ? null : $"{name} has no known status (got '{status}')" }
            .Concat(requirements.Except(covered, StringComparer.Ordinal).Select(id => $"{name} {id} is covered by no acceptance step"))
            .Concat(covered.Except(requirements, StringComparer.Ordinal).Select(id => $"{name} covers {id}, which spec.md does not have"))
            .Concat(steps.Where(step => step.Check != "by hand" && !(status is "draft" or "dropped" ? TestName().IsMatch(step.Check) : tests.Contains(step.Check))).Select(step => $"{name} {step.Id} checks with '{step.Check}', which is neither a test nor 'by hand'"))
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
    [GeneratedRegex(@"\btest(\.(skip|fixme|fail|describe))?\(\s*(?<quote>['""`])(?<name>[A-Z]\w+)\k<quote>\s*,", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PlaywrightTest();

    // A draft names the tests it will need; they exist once it is approved, before any code.
    [GeneratedRegex(@"^[A-Z]\w+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestName();

    // A requirement line of a spec, like "- R3. Each meter turns yellow ...".
    [GeneratedRegex(@"^- (?<id>R\d+)\. ", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Requirement();

    // A test method: a line with [Fact] or [Theory], then lines of other attributes, then the method.
    [GeneratedRegex(@"\[(Fact|Theory)\b.*\r?\n(\s*\[.*\r?\n)*\s*public\s+(async\s+)?(void|Task)\s+(?<name>\w+)\s*\(", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestMethod();

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
        FilesUnder(Root).Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    // Skipped folders are never entered: node_modules alone holds thousands of files.
    private static IEnumerable<string> FilesUnder(string folder) =>
        Directory.EnumerateFiles(folder).Concat(Directory.EnumerateDirectories(folder)
            .Where(child => !SkippedFolders.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            .SelectMany(FilesUnder));

    private static string Relative(string path) => Path.GetRelativePath(Root, path);

    private static bool SaysSomething(string readme) =>
        File.Exists(readme) && File.ReadLines(readme).Any(line => line.Trim().Length > 0 && !line.StartsWith('#'));

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
