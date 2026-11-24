using Microsoft.Web.WebView2.Core;
using Thermi.Ui.Web;

namespace Thermi.Windows.Tests.Window;

/// <summary>What the page's host decides without a WebView2: the type it serves each file as, and how it recovers.</summary>
public sealed class WebPageTests
{
    // Under nosniff, a file served as the wrong type is blocked without a word: every file the build embeds has its own
    // type, and one the host does not know is not served at all.
    [Fact]
    public void EveryFileOfThePageIsServedAsItsOwnTypeAndNoOtherIs()
    {
        var files = typeof(WebPage).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("web/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Equal(
            file.EndsWith(".html", StringComparison.Ordinal),
            WebPage.TypeOf(file)?.StartsWith("text/html", StringComparison.Ordinal)));
        Assert.Null(WebPage.TypeOf("web/assets/unknown.xyz"));
    }

    // A renderer that ended or hangs leaves the window blank for good unless the page loads again; a browser that ended
    // takes the page's controller with it, so the window closes and says so, as it does for a renderer that keeps
    // failing, instead of loading again for ever. WebView2 restarts its other processes itself.
    [Theory]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited, WebPage.MostReloads - 1, "Reload")]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessExited, WebPage.MostReloads, "Close")]
    [InlineData(CoreWebView2ProcessFailedKind.RenderProcessUnresponsive, 0, "Reload")]
    [InlineData(CoreWebView2ProcessFailedKind.FrameRenderProcessExited, 0, "Reload")]
    [InlineData(CoreWebView2ProcessFailedKind.BrowserProcessExited, 0, "Close")]
    [InlineData(CoreWebView2ProcessFailedKind.GpuProcessExited, 0, "None")]
    [InlineData(CoreWebView2ProcessFailedKind.UtilityProcessExited, WebPage.MostReloads, "None")]
    public void APageWhoseProcessFailedLoadsAgainOrItsWindowCloses(CoreWebView2ProcessFailedKind kind, int reloads, string expected)
    {
        Assert.Equal(expected, WebPage.Recovery(kind, reloads).ToString());
    }
}
