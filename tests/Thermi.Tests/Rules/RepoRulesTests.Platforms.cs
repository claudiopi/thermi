using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // The files of the Windows projects that call no Windows-only API, and why each stays there all the same.
    private static readonly Dictionary<string, string> NeedNoWindowsApi = new(StringComparer.Ordinal)
    {
        ["Thermi.Windows/Commands/ThermiCommands.cs"] = "thermi.exe's own command beside every OS's: the mode UAC raises, a Windows program",
        ["Thermi.Windows/Logging/LogEvents.cs"] = "the events of the Windows code's classes, which log nothing else",
        ["Thermi.Windows/Tray/TrayChoices.cs"] = "what a row of the tray's and an overlay's Win32 menus does",
        ["Thermi.Windows/Ui/Views/WindowAgents.cs"] = "the AI agents section's answers from Windows: the machine's and user's PATH, the launcher's folder",
        ["Thermi.Windows/Ui/Web/WindowLinks.cs"] = "opens a file or a link through Windows' Explorer, by its full path",
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
        ["Thermi.Linux/Agents/LinuxAbout.cs"] = "the channel as the files of apt's and dnf's repositories name it, which only Linux's packages write",
        ["Thermi.Linux/Agents/LinuxAgentEnvironment.cs"] = "Linux's answers to the agents' commands, each through a file of its project that calls Linux",
        ["Thermi.Linux/Agents/UserUnit.cs"] = "the start at sign-in as the overlay's systemd user unit, through systemctl, which only Linux has",
        ["Thermi.Linux/Commands/Preview.cs"] = "thermi preview on Linux: the shared preview painted by Linux's Cairo painter, as Windows' paints it with GDI+",
        ["Thermi.Linux/Gnome/ShellSurfaces.cs"] = "GNOME's surfaces, thermi's Shell extension, spoken to through the link to it on the session's bus",
        ["Thermi.Linux/Gnome/ShellIcons.cs"] = "thermi's icons in GNOME's top bar, which only its Shell extension shows",
        ["Thermi.Linux/Hotkeys/KeyNames.cs"] = "a hotkey by the names GTK, the GlobalShortcuts portal and X11's keysyms give its keys",
        ["Thermi.Linux/Tray/DesktopTray.cs"] = "Core's tray icons on a Linux desktop, through the Shell's extension or StatusNotifierItem",
        ["Thermi.Linux/Tray/TrayClicks.cs"] = "a click or a double-click on a tray icon, as Windows tells them and no Linux tray does",
        ["Thermi.Linux/Tray/ThermiNotices.cs"] = "Core's notices as the desktop's own notifications, by the action ids of their buttons",
        ["Thermi.Linux/Tray/TrayIcons.cs"] = "what a Linux desktop's tray shows of thermi's icons, which each desktop serves its own way",
        ["Thermi.Linux/Logging/LinuxLog.cs"] = "the overlay's log events on Linux, which only its host raises",
        ["Thermi.Linux/Overlay/DragShow.cs"] = "a drag as Linux's surfaces show it, its pill and marks each a picture of its own over the windows",
        ["Thermi.Linux/Overlay/IOverlaySurfaces.cs"] = "where a Linux desktop shows thermi's pictures: GNOME's extension, layer surfaces or X11 windows",
        ["Thermi.Linux/Overlay/OverlayParts.cs"] = "what the overlays on Linux share on the host's GLib main loop, the desktop's surfaces among it",
        ["Thermi.Linux/Overlay/OverlayPointer.cs"] = "the pointer on an overlay as Linux's surfaces tell it, each event by the kind the extension names",
        ["Thermi.Linux/Window/LinuxFixes.cs"] = "the overlay and the sensor service started through systemd and polkit, which only Linux has",
        ["Thermi.Linux/Window/WindowPainter.cs"] = "the window's previews painted by Linux's Cairo painter, as Windows' Painter paints them with GDI+",
        ["Thermi.Linux/X11/RootWatch.cs"] = "what an X11 window manager says on the root window, read on thermi's own XCB connection",
        ["Thermi.Sensors.Linux/Reading/ChipKinds.cs"] = "what each of the kernel's hwmon drivers measures, by the name only Linux gives it",
        ["Thermi.Sensors.Linux/Reading/ChipPace.cs"] = "how often a board's hwmon chip is read, by what its Linux driver costs the CPU",
        ["Thermi.Sensors.Linux/Reading/Counters.cs"] = "the kernel's counters turned into changes, which no other OS's service reads",
        ["Thermi.Sensors.Linux/Serving/SensorCommands.cs"] = "the Linux service's own command line, whose trees and roots are the kernel's files",
    };

    // The libraries a Linux project calls by hand, and why: GirCore generates the rest from GObject's introspection data
    // (docs/adr/0019-linux.md), as CsWin32 does Win32's.
    private static readonly Dictionary<string, string> DeclaredByHand = new(StringComparer.Ordinal)
    {
        ["libnvidia-ml.so.1"] = "NVML, NVIDIA's driver library, has no introspection data, and loads only where the driver is installed",
        ["libgtk4-layer-shell.so.0"] = "gtk4-layer-shell's few calls, which GirCore does not ship",
        ["libc"] = "dlopen alone, to load gtk4-layer-shell before GTK, which .NET's NativeLibrary cannot load for every library after",
        ["libX11.so.6"] = "Xlib's few calls for an overlay's window on X11, which has no introspection data",
        ["libgtk-4.so.1"] = "GDK's X11 calls, gdk_x11_*, which GTK's introspection data leaves out",
        ["libXext.so.6"] = "X11's input shape of an overlay's window, which GDK 4 does not set from its input region",
        ["libxcb.so.1"] = "XCB's few calls to grab hotkeys and watch the root window on X11, on connections of thermi's own; it has no introspection data",
        ["libXrandr.so.2"] = "RandR's EDID of a monitor's output on X11, which GDK does not tell, for its id",
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
