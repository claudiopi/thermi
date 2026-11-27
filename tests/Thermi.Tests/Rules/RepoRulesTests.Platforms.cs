using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // The files of the Windows projects that call no Windows-only API, and why each stays there all the same.
    private static readonly Dictionary<string, string> NeedNoWindowsApi = new(StringComparer.Ordinal)
    {
        ["Thermi.Windows/Commands/ThermiCommands.cs"] = "thermi.exe's command line: the overlay, the window and preview, each a Windows program",
        ["Thermi.Windows/Gestures/HotkeyPresses.cs"] = "what thermi's hotkeys do, which Windows' RegisterHotKey delivers anywhere in Windows",
        ["Thermi.Windows/Logging/LogEvents.cs"] = "the events of the Windows code's classes, which log nothing else",
        ["Thermi.Windows/Tray/TrayChoices.cs"] = "what a row of the tray's and an overlay's Win32 menus does",
        ["Thermi.Windows/Ui/Views/OverlayPlaces.cs"] = "where each overlay's window sits, measured as its Windows painter keeps its room",
        ["Thermi.Windows/Ui/Views/PageWatch.cs"] = "what the window's bridge keeps between reads, asking the overlay's window for its hotkeys",
        ["Thermi.Windows/Ui/Views/WindowAgents.cs"] = "the AI agents section's answers from Windows: the machine's and user's PATH, the launcher's folder",
        ["Thermi.Windows/Ui/Web/WindowLinks.cs"] = "opens a file or a link through Windows' Explorer, by its full path",
        ["Thermi.Windows/Ui/Web/WindowSession.cs"] = "the settings window's thread: its WebView2 window, editor, bridge and beacon",
        ["Thermi.Windows/Updates/ThermiPaths.cs"] = "Windows' install layout: Program Files, the per-user folder, the .exe names",
        ["Thermi.Windows/Updates/WindowUpdates.cs"] = "About's side of the Windows updater: its check and restart through the service",
        ["Thermi.Sensors.Windows/Install/AdoptUndo.cs"] = "what the MSI's rollback puts back: the services' image paths, Installed apps' entry",
        ["Thermi.Sensors.Windows/Install/ISystemSetup.cs"] = "what an install changes in Windows: the services, sc.exe, Installed apps",
        ["Thermi.Sensors.Windows/Install/SetupRefusedException.cs"] = "sc.exe's refusal, which ISystemSetup throws",
        ["Thermi.Sensors.Windows/Reading/DeviceUpdates.cs"] = "LibreHardwareMonitor's devices updated in turn, each within its patience",
        ["Thermi.Sensors.Windows/Reading/ReadPace.cs"] = "LibreHardwareMonitor's update pace and its speeds, for its devices alone",
        ["Thermi.Sensors.Windows/Reading/ReadingRules.cs"] = "what the service mends in what LibreHardwareMonitor reads",
        ["Thermi.Sensors.Windows/Reading/UpdateThreads.cs"] = "the threads LibreHardwareMonitor's updates block on, in a driver",
        ["Thermi.Sensors.Windows/Service/SensorCommands.cs"] = "thermi-sensors' command line: the Windows service, its install and fixes",
        ["Thermi.Sensors.Windows/Updates/ManifestClient.cs"] = "the Windows service's update downloads, which only it makes, as LocalSystem",
        ["Thermi.Sensors.Windows/Updates/Unpacker.cs"] = "the Windows updater's unpacking under Program Files, checked against its signed catalog",
        ["Thermi.Sensors.Windows/Updates/UpdateBuild.cs"] = "the Windows updater's build: its Authenticode signers and the MSI it takes",
    };

    // The files of the Linux projects that call no Linux-only API, and why each stays there all the same.
    private static readonly Dictionary<string, string> NeedNoLinuxApi = new(StringComparer.Ordinal)
    {
        ["Thermi.Linux/Agents/LinuxAgentEnvironment.cs"] = "Linux's answers to the agents' commands, each through a file of its project that calls Linux",
        ["Thermi.Sensors.Linux/Reading/ChipKinds.cs"] = "what each of the kernel's hwmon drivers measures, by the name only Linux gives it",
        ["Thermi.Sensors.Linux/Reading/Counters.cs"] = "the kernel's counters turned into changes, which no other OS's service reads",
        ["Thermi.Sensors.Linux/Serving/SensorCommands.cs"] = "the Linux service's own command line, whose trees and roots are the kernel's files",
    };

    // The libraries a Linux project calls by hand, and why: GirCore generates the rest from GObject's introspection data
    // (docs/adr/0019-linux.md), as CsWin32 does Win32's.
    private static readonly Dictionary<string, string> DeclaredByHand = new(StringComparer.Ordinal)
    {
        ["libnvidia-ml.so.1"] = "NVML, NVIDIA's driver library, has no introspection data, and loads only where the driver is installed",
        ["libgtk4-layer-shell.so.0"] = "gtk4-layer-shell's few calls, which GirCore does not ship",
    };

    // CsWin32 generates every Win32 declaration from a NativeMethods.txt, and GirCore every GObject library's; one written by
    // hand is a layout nobody checks. A Linux project's may call by hand only the libraries listed, by name or by a constant
    // of its file.
    [Fact]
    public void NoWin32CallIsDeclaredByHand()
    {
        var declared = RepoFiles([".cs"]).SelectMany(path =>
        {
            var text = File.ReadAllText(path);
            var linux = LinuxProject().IsMatch(Relative(path).Replace('\\', '/'));
            return File.ReadLines(path)
                .Select((line, index) => (Import: HandImport().Match(line), Number: index + 1))
                .Where(entry => entry.Import.Success && !(linux && DeclaredByHand.ContainsKey(Library(entry.Import.Groups["library"].Value, text))))
                .Select(entry => linux
                    ? $"{Relative(path)}:{entry.Number} declares a call by hand: generate it with GirCore, or list its library here with why"
                    : $"{Relative(path)}:{entry.Number} declares a Win32 call by hand: list it in NativeMethods.txt");
        });

        AssertNone(declared);
    }

    // The library an import names: a string, or a constant its file declares.
    private static string Library(string named, string text)
    {
        var constant = $@"\bconst\s+string\s+{Regex.Escape(named.Split('.')[^1])}\s*=\s*""(?<library>[^""]*)""";
        return named.StartsWith('"') ? named.Trim('"') : Regex.Match(text, constant, RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["library"].Value;
    }

    // The attribute anywhere in a line: qualified, with its Attribute suffix, or after another one in the same brackets.
    [GeneratedRegex(
        @"\b(DllImport|LibraryImport)(Attribute)?\s*\(\s*(?<library>""[^""]*""|[\w.]+)?", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HandImport();

    // A file of a Linux project, or of the Linux tests.
    [GeneratedRegex(@"^(src|tests)/Thermi(\.\w+)*\.Linux(\.Tests)?/", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinuxProject();

    // What every OS's host shares runs on any OS (docs/adr/0018-shared-and-windows-projects.md): it targets plain net10.0,
    // and no project it builds on, however far down, targets Windows alone or is one of Linux's, which target plain net10.0
    // too but run on Linux alone.
    [Fact]
    public void SharedProjectsNeedNoWindows()
    {
        string[] shared = ["Thermi.Core", "Thermi.App", "Thermi.Cli", "Thermi.Client"];

        AssertNone(shared.SelectMany(name => OsOnly(name, Path.Combine(Root, "src", name, $"{name}.csproj"))));
    }

    // The project and every project it references, each whose target framework is not plain .NET, or that is Linux's.
    private static IEnumerable<string> OsOnly(string shared, string project)
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

            if (Path.GetFileNameWithoutExtension(next).EndsWith(".Linux", StringComparison.Ordinal))
            {
                yield return $"{shared} builds on {Relative(next)}, which runs on Linux alone";
            }

            foreach (var reference in xml.Descendants().Where(node => node.Name.LocalName == "ProjectReference"))
            {
                var path = reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar);
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(next)!, path)));
            }
        }
    }

    // The Windows projects hold what only Windows runs (docs/adr/0018-shared-and-windows-projects.md): each file uses a
    // Windows-only API, or declares a type that derives from or implements a type of its project that does, or is listed
    // above with why it stays. Naming such a type is not enough: logic any OS shares goes to Core, Thermi.App or Thermi.Cli.
    [Fact]
    public void WindowsProjectsHoldOnlyWhatNeedsWindows() => AssertNone(OsProjectFindings("Windows", WindowsApi(), NeedNoWindowsApi));

    // The Linux projects, likewise, hold what only Linux runs (docs/adr/0019-linux.md): GirCore's GTK, WebKit, Cairo, Pango
    // and D-Bus, the kernel's files, the socket and its peer's credentials, systemd, NVML and gtk4-layer-shell.
    [Fact]
    public void LinuxProjectsHoldOnlyWhatNeedsLinux()
    {
        Assert.NotEmpty(Directory.EnumerateDirectories(Path.Combine(Root, "src"), "*.Linux"));
        AssertNone(OsProjectFindings("Linux", LinuxApi(), NeedNoLinuxApi));
    }

    // Each file of the projects named for the OS that uses no API of its own, nor declares a type that derives from or
    // implements a type of its project that does, and is not listed with why; and each listed one that is gone or uses one.
    private static IEnumerable<string> OsProjectFindings(string os, Regex api, Dictionary<string, string> listed)
    {
        var files = Directory.EnumerateDirectories(Path.Combine(Root, "src"), $"*.{os}")
            .SelectMany(project => FilesUnder(project).Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
                .Select(path => (Name: Path.GetRelativePath(Path.Combine(Root, "src"), path).Replace('\\', '/'),
                    Project: Path.GetFileName(project), Code: CodeComment().Replace(File.ReadAllText(path), ""))))
            .ToList();
        var tied = files.Where(file => api.IsMatch(file.Code)).Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        for (var grew = true; grew;)
        {
            var types = files.Where(file => tied.Contains(file.Name))
                .SelectMany(file => DeclaredType().Matches(file.Code).Select(match => (file.Project, Type: match.Groups["type"].Value)))
                .ToHashSet();
            var more = files.Where(file => !tied.Contains(file.Name) && BaseList().Matches(file.Code).Any(declared => types.Any(type =>
                    type.Project == file.Project
                    && Regex.IsMatch(declared.Groups["bases"].Value, $@"\b{type.Type}\b", RegexOptions.None, TimeSpan.FromSeconds(1)))))
                .Select(file => file.Name).ToList();
            tied.UnionWith(more);
            grew = more.Count > 0;
        }

        var names = files.Select(file => file.Name).ToHashSet(StringComparer.Ordinal);
        return names.Where(name => !tied.Contains(name) && !listed.ContainsKey(name))
            .Select(name => $"src/{name} uses no {os}-only API: move it to a shared project, or list it here with why it stays")
            .Concat(listed.Keys.Where(name => !names.Contains(name) || tied.Contains(name))
                .Select(name => $"src/{name} is listed as needing no {os}-only API, but it is gone or uses one: drop it from the list"));
    }

    [GeneratedRegex(
        @"\bWindows\.Win32\b|\bPInvoke\.|\bMicrosoft\.Win32\b|\bMicrosoft\.Web\.WebView2\b|\bSystem\.Drawing\b|\bSystem\.ServiceProcess\b"
            + @"|\bSystem\.Security\.(AccessControl|Principal)\b|\bEventLog\w*|\bLibreHardwareMonitor\b|\bThermi\.Trust\b|""runas"""
            + @"|\bSupportedOSPlatform\(""windows",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex WindowsApi();

    // GirCore's namespaces, the kernel's files, a Unix socket and its peer's credentials, the platform's attribute, systemd's
    // command and the variables it hands a service, and the libraries that only Linux has. A word in a plain string is no API.
    [GeneratedRegex(
        @"\b(Gtk|Gdk|Gio|GLib|Cairo|Pango|PangoCairo|WebKit)(\.|\s*;)|""/(sys|proc)/|\bUnixDomainSocketEndPoint\b|\bSO_PEERCRED\b"
            + @"|\bSupportedOSPlatform\(""linux""|""(systemctl|LISTEN_FDS|LISTEN_PID|NOTIFY_SOCKET)"""
            + @"|\b(?i:nvml)\w*|\blibnvidia-ml\b|\bgtk4-layer-shell\b|\bgtk_layer_\w+",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinuxApi();

    [GeneratedRegex(@"\b(class|record|struct|interface|enum)\s+(?<type>\w+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DeclaredType();

    // A type's declaration up to its body: what follows the colon after its name, type parameters and primary constructor.
    [GeneratedRegex(
        @"\b(class|record|struct|interface)\s+\w+\s*(<[^>]*>)?\s*(\((?>[^()]+|\((?<open>)|\)(?<-open>))*\))?\s*:(?<bases>[^{;]+)",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex BaseList();

    [GeneratedRegex(@"//.*$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeComment();
}
