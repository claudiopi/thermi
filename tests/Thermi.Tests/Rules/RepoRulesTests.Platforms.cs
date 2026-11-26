using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // The files of the Windows projects that call no Windows-only API, and why each stays there all the same.
    private static readonly Dictionary<string, string> NeedNoWindowsApi = new(StringComparer.Ordinal)
    {
        ["Thermi.Windows/Logging/LogEvents.cs"] = "the events of the Windows code's classes, which log nothing else",
        ["Thermi.Windows/Ui/Web/WindowLinks.cs"] = "opens a file or a link through Windows' Explorer, by its full path",
        ["Thermi.Windows/Updates/ThermiPaths.cs"] = "Windows' install layout: Program Files, the per-user folder, the .exe names",
        ["Thermi.Sensors.Windows/Install/AdoptUndo.cs"] = "what the MSI's rollback puts back: the services' image paths, Installed apps' entry",
        ["Thermi.Sensors.Windows/Install/ISystemSetup.cs"] = "what an install changes in Windows: the services, sc.exe, Installed apps",
        ["Thermi.Sensors.Windows/Install/SetupRefusedException.cs"] = "sc.exe's refusal, which ISystemSetup throws",
        ["Thermi.Sensors.Windows/Reading/ReadPace.cs"] = "LibreHardwareMonitor's update pace and its speeds, for its devices alone",
        ["Thermi.Sensors.Windows/Reading/ReadingRules.cs"] = "what the service mends in what LibreHardwareMonitor reads",
        ["Thermi.Sensors.Windows/Reading/UpdateThreads.cs"] = "the threads LibreHardwareMonitor's updates block on, in a driver",
        ["Thermi.Sensors.Windows/Recording/SeriesRecorder.cs"] =
            "thermi-sensors' --dump --seconds: the series is Core's SnapshotJson, which another OS's host writes alike",
        ["Thermi.Sensors.Windows/Service/SensorCommands.cs"] = "thermi-sensors' command line: the Windows service, its install and fixes",
        ["Thermi.Sensors.Windows/Updates/UpdateBuild.cs"] = "the Windows updater's build: its Authenticode signers and the MSI it takes",
    };

    // What every OS's host shares runs on any OS (docs/adr/0018-shared-and-windows-projects.md): it targets plain net10.0,
    // and no project it builds on, however far down, targets Windows alone.
    [Fact]
    public void SharedProjectsNeedNoWindows()
    {
        string[] shared = ["Thermi.Core", "Thermi.App", "Thermi.Cli", "Thermi.Client"];

        AssertNone(shared.SelectMany(name => WindowsOnly(name, Path.Combine(Root, "src", name, $"{name}.csproj"))));
    }

    // The project and every project it references, each whose target framework is not plain .NET.
    private static IEnumerable<string> WindowsOnly(string shared, string project)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>([Path.GetFullPath(project)]);
        while (pending.TryPop(out var next))
        {
            if (!seen.Add(next))
            {
                continue;
            }

            var xml = XDocument.Load(next);
            var target = string.Concat(xml.Descendants()
                .Where(node => node.Name.LocalName is "TargetFramework" or "TargetFrameworks").Select(node => node.Value));
            if (target.Length == 0 || target.Contains("-windows", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{shared} builds on {Relative(next)}, which targets {(target.Length == 0 ? "no framework of its own" : target)}";
            }

            foreach (var reference in xml.Descendants().Where(node => node.Name.LocalName == "ProjectReference"))
            {
                var path = reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(next)!, path)));
            }
        }
    }

    // The Windows projects hold what only Windows runs (docs/adr/0018-shared-and-windows-projects.md): each file uses a
    // Windows-only API, or a type of its project that does, or is listed above with why it stays. Logic any OS shares goes
    // to Core, Thermi.App or Thermi.Cli instead.
    [Fact]
    public void WindowsProjectsHoldOnlyWhatNeedsWindows()
    {
        var files = Directory.EnumerateDirectories(Path.Combine(Root, "src"), "*.Windows")
            .SelectMany(project => FilesUnder(project).Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
                .Select(path => (Name: Path.GetRelativePath(Path.Combine(Root, "src"), path).Replace('\\', '/'),
                    Project: Path.GetFileName(project), Code: CodeComment().Replace(File.ReadAllText(path), ""))))
            .ToList();
        var tied = files.Where(file => WindowsApi().IsMatch(file.Code)).Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        for (var grew = true; grew;)
        {
            var types = files.Where(file => tied.Contains(file.Name))
                .SelectMany(file => DeclaredType().Matches(file.Code).Select(match => (file.Project, Type: match.Groups["type"].Value)))
                .ToHashSet();
            var more = files.Where(file => !tied.Contains(file.Name)
                && types.Any(type => type.Project == file.Project && Regex.IsMatch(file.Code, $@"\b{type.Type}\b", RegexOptions.None, TimeSpan.FromSeconds(1))))
                .Select(file => file.Name).ToList();
            tied.UnionWith(more);
            grew = more.Count > 0;
        }

        var names = files.Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        AssertNone(names.Where(name => !tied.Contains(name) && !NeedNoWindowsApi.ContainsKey(name))
            .Select(name => $"src/{name} uses no Windows-only API: move it to a shared project, or list it here with why it stays")
            .Concat(NeedNoWindowsApi.Keys.Where(name => !names.Contains(name) || tied.Contains(name))
                .Select(name => $"src/{name} is listed as needing no Windows-only API, but it is gone or uses one: drop it from the list")));
    }

    [GeneratedRegex(
        @"\bWindows\.Win32\b|\bPInvoke\.|\bMicrosoft\.Win32\b|\bMicrosoft\.Web\.WebView2\b|\bSystem\.Drawing\b|\bSystem\.ServiceProcess\b"
            + @"|\bSystem\.Security\.(AccessControl|Principal)\b|\bEventLog\w*|\bLibreHardwareMonitor\b|\bThermi\.Trust\b|""runas"""
            + @"|\bSupportedOSPlatform\(""windows",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WindowsApi();

    [GeneratedRegex(@"\b(class|record|struct|interface|enum)\s+(?<type>\w+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DeclaredType();

    [GeneratedRegex(@"//.*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeComment();
}
