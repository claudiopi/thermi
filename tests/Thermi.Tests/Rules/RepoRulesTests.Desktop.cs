using System.Text.RegularExpressions;

namespace Thermi.Tests.Rules;

public sealed partial class RepoRulesTests
{
    // What makes a window, or Windows' window for a process, wherever a test may call it.
    private static readonly string[] MakesWindows =
    [
        "new OverlayHost(",
        "new SettingsBeacon(",
        "new LayeredTestWindow(",
        "new MenuWatcher(",
        "new OtherProcess(",
        "WindowSession.Run(",
        "WindowProcess.RunAsync(",
        "HiddenWindow.Create(",
        "TopLevelWindow.Create(",
    ];

    // Helpers that make windows only for a caller already on its own desktop, or that join one before they make any; a
    // test run through HostRun.Run, or through the Run of OverlayPresses, which calls it, is on its own desktop.
    private static readonly string[] OnTheirCallersDesktop = ["TestWindows.cs", "MenuWatcher.cs", "OtherProcess.cs"];

    // The one file of each OS's tests that starts processes: Windows' on a desktop of the tests' own, Linux's and macOS's in
    // a session of the test's own.
    private static readonly string[] StartsProcesses =
    [
        Path.Combine("tests", "Thermi.Windows.Tests", "Processes", "DesktopProcess.cs"),
        Path.Combine("tests", "Thermi.Linux.Tests", "OwnSession.cs"),
        Path.Combine("tests", "Thermi.MacOS.Tests", "OwnSession.cs"),
    ];

    // What Linux's session sets anew for each process it starts, none of it the person's: its runtime folder, its Wayland
    // and X11 displays and its D-Bus session bus, over an environment cleared of theirs.
    private static readonly string[] SessionOwns =
        ["start.Environment.Clear();", "[\"XDG_RUNTIME_DIR\"]", "[\"WAYLAND_DISPLAY\"]", "[\"DISPLAY\"]", "[\"DBUS_SESSION_BUS_ADDRESS\"]"];

    // What the Mac's session sets anew, a home and a temporary folder over an environment cleared of the person's, and how it
    // keeps thermi and the Mac's tools that show or load agents to a session made for tests: they start in one only, which
    // THERMI_TEST_SESSION names, and anywhere else the test skips. The Mac has one screen and one launchd for its user, which
    // no session of a test's own holds apart, as a Linux compositor or a Windows desktop does.
    private static readonly string[] MacSessionOwns =
    [
        "start.Environment.Clear();", "[\"HOME\"]", "[\"TMPDIR\"]", "MadeForTests = \"THERMI_TEST_SESSION\"", "Assert.SkipUnless(ForTests",
        "OnTheScreen = [\"thermi\", \"launchctl\", \"open\", \"screencapture\", \"osascript\"", "!_onScreen && OnTheScreen.Contains(",
    ];

    // No test shows anything on the user's desktop, nor starts a process there: every process starts through
    // DesktopProcess, on a desktop of the tests' own, or on Linux, a compositor among them, and on macOS, through OwnSession,
    // in a session of the test's own, which on the Mac shows thermi only in a session made for tests; each async Linux and
    // macOS test is bounded; and every test file that makes a window makes it on one too.
    // The SDK's public constructors start thermi-cli as the process that calls them, so a test reads through them only from
    // a process of the session's; the internal one, which takes how to find and run it, starts nothing of its own.
    [Fact]
    public void TestsMakeWindowsAndProcessesOnlyOnADesktopOfTheirOwn()
    {
        var tests = FilesUnder(Path.Combine(Root, "tests"))
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
            .Select(file => (Path: Relative(file), Name: Path.GetFileName(file), Text: File.ReadAllText(file)))
            .Where(file => file.Name != "RepoRulesTests.Desktop.cs")
            .ToList();

        var starting = tests
            .Where(file => !StartsProcesses.Contains(file.Path, StringComparer.Ordinal)
                && Regex.IsMatch(
                    file.Text,
                    @"(?<!\w)(Process\.Start\(|ProcessStartInfo|CreateProcess\(|new Process\b|new ThermiClient\((?!\(\)\s*=>))",
                    RegexOptions.ExplicitCapture,
                    TimeSpan.FromSeconds(1)))
            .Select(file => $"{file.Path} starts a process: start it through DesktopProcess on Windows, or OwnSession on Linux and macOS");
        var showing = tests
            .Where(file => !OnTheirCallersDesktop.Contains(file.Name)
                && MakesWindows.Any(make => file.Text.Contains(make, StringComparison.Ordinal))
                && !new[] { "OnOwnDesktop(", "HostRun.Run(", ": OverlayPresses" }.Any(own => file.Text.Contains(own, StringComparison.Ordinal)))
            .Select(file => $"{file.Path} makes a window off a desktop of its own");
        var session = tests.Single(file => file.Path == StartsProcesses[1]).Text;
        var mac = tests.Single(file => file.Path == StartsProcesses[2]).Text;
        var shared = SessionOwns.Where(owned => !session.Contains(owned, StringComparison.Ordinal))
            .Select(owned => $"{StartsProcesses[1]} does not set {owned}, so its processes would take the person's")
            .Concat(MacSessionOwns.Where(owned => !mac.Contains(owned, StringComparison.Ordinal))
                .Select(owned => $"{StartsProcesses[2]} lacks {owned}, so its processes could take the person's, or show on their screen"));
        // A program that never writes what a test waits for would hang the run, and the container or the Mac it holds, forever.
        string[] bounded = [Path.Combine("tests", "Thermi.Linux.Tests"), Path.Combine("tests", "Thermi.MacOS.Tests")];
        var unbounded = tests.Where(file => bounded.Any(folder => file.Path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .SelectMany(file => AsyncTest().Matches(file.Text)
                .Where(test => !test.Groups["args"].Value.Contains("Timeout = Bound.Test", StringComparison.Ordinal))
                .Select(test => $"{file.Path}: {test.Groups["name"].Value} has no Timeout = Bound.Test, so a wait of its could hang the run"));

        AssertNone(starting.Concat(showing).Concat(shared).Concat(unbounded));
    }

    // A test's attribute, the rows and comments under it, and its async method's name.
    [GeneratedRegex(
        @"\[(Fact|Theory)(?<args>\([^)\r\n]*\))?\][^\r\n]*\r?\n(\s*(\[|//)[^\r\n]*\r?\n)*\s*public async Task (?<name>\w+)",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex AsyncTest();
}
