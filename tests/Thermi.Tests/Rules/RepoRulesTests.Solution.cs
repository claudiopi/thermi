using System.IO.Enumeration;
using System.Xml.Linq;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    private static readonly string[] LockFiles = ["packages.lock.json", "package-lock.json"];

    // A developer opens thermi.slnx and expects all of thermi there. A project the solution cannot build as one, the page's
    // and the MSI's, is a folder of its top files, all but the lock files.
    [Fact]
    public void TheSolutionHoldsEveryProject()
    {
        var solution = XDocument.Load(Path.Combine(Root, "thermi.slnx"));
        var listed = solution.Descendants().Select(node => node.Attribute("Path")?.Value).OfType<string>()
            .Select(path => path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ProjectFolders()
            .SelectMany(project => Directory.EnumerateFiles(project).Where(file => Shown(project, file) && !Ignored(file)))
            .Select(file => Relative(file).Replace('\\', '/'))
            .Where(file => !listed.Contains(file))
            .Select(file => $"thermi.slnx does not list {file}");
        var gone = listed
            .Where(path => !File.Exists(Path.Combine(Root, path)))
            .Select(path => $"thermi.slnx lists {path}, which does not exist");

        AssertNone(missing.Concat(gone));
    }

    // AGENTS.md's map is where a newcomer finds each project: it has a row for every one, and the paths rule holds its rows.
    [Fact]
    public void TheMapNamesEveryProject()
    {
        var map = File.ReadAllText(Path.Combine(Root, "AGENTS.md"));
        var unnamed = ProjectFolders()
            .Select(project => Relative(project).Replace('\\', '/'))
            .Where(project => !map.Contains($"| `{project}` |", StringComparison.Ordinal))
            .Select(project => $"AGENTS.md's map has no row for {project}");

        AssertNone(unnamed);
    }

    // A project is a folder with a .NET, WiX or npm project file of its own.
    private static IEnumerable<string> ProjectFolders() =>
        RepoFiles([".csproj", ".wixproj", ".json"])
            .Where(path => Path.GetExtension(path) != ".json" || Path.GetFileName(path) == "package.json")
            .Select(path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    // What git ignores is no file of the project's, such as the TypeScript build's tsbuildinfo: a file name, or a pattern of
    // one with a star, from the root's .gitignore or the project's own.
    private static bool Ignored(string file)
    {
        var patterns = new[] { Root, Path.GetDirectoryName(file)! }
            .Select(folder => Path.Combine(folder, ".gitignore"))
            .Where(File.Exists)
            .SelectMany(File.ReadLines)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.Contains('/', StringComparison.Ordinal));
        var name = Path.GetFileName(file);
        return patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, name));
    }

    // A .NET project shows as its project file; any other as its files.
    private static bool Shown(string project, string file) =>
        Directory.EnumerateFiles(project, "*.csproj").Any()
            ? Path.GetExtension(file) == ".csproj"
            : !LockFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal);
}
