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
    // test run through HostRun.Run is on its own desktop.
    private static readonly string[] OnTheirCallersDesktop = ["TestWindows.cs", "MenuWatcher.cs", "OtherProcess.cs"];

    // No test shows anything on the user's desktop, nor starts a process there: every process starts through
    // DesktopProcess, on a desktop of the tests' own, and every test file that makes a window makes it on one too.
    [Fact]
    public void TestsMakeWindowsAndProcessesOnlyOnADesktopOfTheirOwn()
    {
        var tests = Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (Name: Path.GetFileName(file), Text: File.ReadAllText(file)))
            .Where(file => file.Name != "RepoRulesTests.Desktop.cs")
            .ToList();

        var starting = tests
            .Where(file => file.Name != "DesktopProcess.cs"
                && Regex.IsMatch(
                    file.Text,
                    @"(?<!\w)(Process\.Start\(|ProcessStartInfo|CreateProcess\()",
                    RegexOptions.ExplicitCapture,
                    TimeSpan.FromSeconds(1)))
            .Select(file => file.Name);
        var showing = tests
            .Where(file => !OnTheirCallersDesktop.Contains(file.Name)
                && MakesWindows.Any(make => file.Text.Contains(make, StringComparison.Ordinal))
                && !new[] { "OnOwnDesktop(", "HostRun.Run(" }.Any(own => file.Text.Contains(own, StringComparison.Ordinal)))
            .Select(file => file.Name);

        Assert.Empty(starting.Concat(showing));
    }
}
