using System.Text;
using Thermi.Core.Bridge;
using Thermi.Linux.Window;

namespace Thermi.Linux.Tests.Window;

/// <summary>What the settings window serves the page and lets it reach: its own files, on its own origin, and nothing else.</summary>
public sealed class PageServingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("thermi-page-").FullName;

    public PageServingTests()
    {
        // The page's folder, a file beside it, a folder whose name starts as the page's does, and a file of the page's that
        // cannot be read now, as while the package manager replaces it; each holds its own name.
        string[] files =
        [
            Path.Combine("web", PageFiles.Index), Path.Combine("web", "assets", "page.js"), "beside.txt", Path.Combine("web-other", "x.js"),
            Path.Combine("web", "locked.js"),
        ];
        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, file))!);
            File.WriteAllText(Path.Combine(_root, file), file);
        }

        File.SetUnixFileMode(Path.Combine(_root, files[^1]), UnixFileMode.None);
    }

    // A request reads a file of the page's folder, the page itself for none, and nothing outside it, however its path is
    // written, nor what the folder does not hold, nor what it cannot read, which the page gets as a file it lacks.
    [Theory] // Rows: the page, a file of it, a path out of the folder, one into a folder named as it starts, a file it lacks, and one unread.
    [InlineData("/", "web/index.html")]
    [InlineData("/assets/page.js", "web/assets/page.js")]
    [InlineData("/../beside.txt", null)]
    [InlineData("/../web-other/x.js", null)]
    [InlineData("/missing.js", null)]
    [InlineData("/locked.js", null)]
    public void ThePageIsServedOnlyFromItsFolder(string path, string? served)
    {
        var files = Path.Combine(_root, "web");

        var read = PageServer.Read(files, path);

        Assert.Equal(served is null ? null : Path.Combine(served.Split('/')), read is { } file ? Encoding.UTF8.GetString(file.Content) : null);
    }

    // The page navigates within thermi's origin and nowhere else, and opens no window of its own, even on its origin.
    [Theory] // Rows: a navigation on the origin, one off it, a new window, and a response.
    [InlineData(WebKit.PolicyDecisionType.NavigationAction, SettingsWindow.Origin + "index.html", true)]
    [InlineData(WebKit.PolicyDecisionType.NavigationAction, "https://example.com/", false)]
    [InlineData(WebKit.PolicyDecisionType.NewWindowAction, SettingsWindow.Origin + "index.html", false)]
    [InlineData(WebKit.PolicyDecisionType.Response, null, true)]
    public void ThePageGoesNowhereButItsOrigin(WebKit.PolicyDecisionType type, string? uri, bool allowed) =>
        Assert.Equal(allowed, SettingsWindow.Allowed(type, uri));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
