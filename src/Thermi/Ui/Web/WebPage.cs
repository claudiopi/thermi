using System.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Thermi.Core.Bridge;
using Thermi.Logging;
using Windows.Win32.Foundation;

namespace Thermi.Ui.Web;

/// <summary>
/// The page in WebView2: its files, embedded in thermi.exe, served from a host name that never resolves, under a policy
/// that loads nothing from anywhere else; no navigation away, no new window, no browser keys or menus. The page reaches
/// C# only through <c>window.thermi</c>, which a script set before the page's own answers with the bridge.
/// </summary>
internal sealed class WebPage(IWindowBridge bridge, ILogger log) : IDisposable
{
    /// <summary>Reserved never to resolve, so a request that escapes the page's own files reaches no one.</summary>
    public const string Origin = "https://thermi.invalid";

    // A second line of defense behind the page's own policy in index.html, which keeps the only style it allows, the
    // empty element Fluent fills: this one, sent with every file, forbids any other origin even if that one changed.
    private const string Policy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'";

    /// <summary>Raised once the page has loaded, before its first frame shows.</summary>
    public event EventHandler? Loaded;

    private bool _shown;
    private bool _minimized;

    public CoreWebView2Controller? Controller { get; private set; }

    /// <summary>
    /// Starts WebView2 in <paramref name="window"/>, its runtime from <paramref name="browserFolder"/> or the installed one,
    /// its profile kept in <paramref name="dataFolder"/>, and loads the page; without <paramref name="mica"/>, the page is
    /// marked to paint Windows' solid background instead. Throws <see cref="WebView2RuntimeNotFoundException"/> without a runtime.
    /// </summary>
    public async Task OpenAsync(HWND window, string? browserFolder, string dataFolder, bool trace, bool mica)
    {
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = false, IsCustomCrashReportingEnabled = true };
        var environment = await CoreWebView2Environment.CreateAsync(browserFolder, dataFolder, options);
        Controller = await environment.CreateCoreWebView2ControllerAsync(window);
        // Made under a window not shown yet, WebView2's own windows stay hidden until told otherwise: the page is shown
        // with the window, through Show.
        Controller.IsVisible = false;
        Controller.DefaultBackgroundColor = Color.Transparent;
        var view = Controller.CoreWebView2;
        Lock(view.Settings, trace);
        view.AddWebResourceRequestedFilter($"{Origin}/*", CoreWebView2WebResourceContext.All);
        view.WebResourceRequested += (_, e) => e.Response = Serve(view.Environment, e.Request.Uri);
        view.NavigationStarting += (_, e) => e.Cancel = !e.Uri.StartsWith($"{Origin}/", StringComparison.Ordinal);
        view.NewWindowRequested += (_, e) => e.Handled = true;
        view.NavigationCompleted += (_, _) => Loaded?.Invoke(this, EventArgs.Empty);
        view.ProcessFailed += (_, e) => log.PageFailed(e.ProcessFailedKind.ToString());
        view.WebMessageReceived += Received;
        bridge.Changed += (_, _) => Raise(nameof(IWindowBridge.Changed), null);
        bridge.Readings += (_, _) => Raise(nameof(IWindowBridge.Readings), null);
        bridge.Picked += (_, pick) => Raise(nameof(IWindowBridge.Picked), pick);
        await view.AddScriptToExecuteOnDocumentCreatedAsync(BridgeCalls.Client);
        // The document has no element yet when this runs; it has one, styled before the window shows, once parsed. Over
        // Mica the page lets it show, as it does around the caption buttons; without it, it paints Windows' solid one.
        var backdrop = mica ? "mica" : "solid";
        await view.AddScriptToExecuteOnDocumentCreatedAsync($"addEventListener('DOMContentLoaded', () => {{ document.documentElement.dataset.backdrop = '{backdrop}'; }});");

        view.Navigate($"{Origin}/index.html");
    }

    /// <summary>Shows the page, once its window shows.</summary>
    public void Show()
    {
        _shown = true;
        Follow();
    }

    /// <summary>
    /// The window minimized or back: hidden, the page draws nothing, and reads nothing either, as it does while
    /// `document.hidden`, until it shows again.
    /// </summary>
    public void Minimized(bool minimized)
    {
        _minimized = minimized;
        Follow();
    }

    private void Follow()
    {
        if (Controller is { } controller)
        {
            controller.IsVisible = _shown && !_minimized;
        }
    }

    /// <summary>Tells the page where Windows' caption buttons are, in CSS pixels, so its title row leaves them room.</summary>
    public void Caption(double width, double height)
    {
        var script = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"document.documentElement.style.setProperty('--thermi-caption-width','{width}px');document.documentElement.style.setProperty('--thermi-caption-height','{height}px');");
        _ = Controller?.CoreWebView2.ExecuteScriptAsync(script);
    }

    public void Dispose() => Controller?.Close();

    // What a person could do to a browser and not to a window: reload, zoom, print, find, open the browser's menus or
    // tools, save passwords. The tools stay with --trace, to look into a fault.
    private static void Lock(CoreWebView2Settings settings, bool trace)
    {
        settings.AreDevToolsEnabled = trace;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsReputationCheckingRequired = false;
        settings.IsNonClientRegionSupportEnabled = true;
        settings.IsWebMessageEnabled = true;
    }

    private static CoreWebView2WebResourceResponse Serve(CoreWebView2Environment environment, string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? parsed.AbsolutePath.TrimStart('/') : "";
        var file = typeof(WebPage).Assembly.GetManifestResourceStream($"web/{(path.Length == 0 ? "index.html" : path)}");
        if (file is null)
        {
            return environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }

        var headers = $"Content-Type: {TypeOf(path)}\r\nContent-Security-Policy: {Policy}\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store";
        return environment.CreateWebResourceResponse(file, 200, "OK", headers);
    }

    private static string TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".js" => "text/javascript",
        ".css" => "text/css",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".json" => "application/json",
        _ => "text/html; charset=utf-8",
    };

    private void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith($"{Origin}/", StringComparison.Ordinal))
        {
            return;
        }

        _ = AnswerAsync(e.WebMessageAsJson);
    }

    private async Task AnswerAsync(string call)
    {
        string answer;
        try
        {
            answer = await BridgeCalls.AnswerAsync(bridge, call);
        }
#pragma warning disable CA1031 // Logged, and the page's promise rejected: a fault in one call never takes the window down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.PageCallFailed(call, ex);
            answer = BridgeCalls.Failed(call, ex.Message);
        }

        Controller?.CoreWebView2.PostWebMessageAsJson(answer);
    }

    private void Raise(string name, object? value) => Controller?.CoreWebView2.PostWebMessageAsJson(BridgeCalls.EventOf(name, value));
}
