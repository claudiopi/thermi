using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.Window;
using Thermi.Core.Bridge;
using Thermi.Core.Placement;
using Thermi.Core.Settings;

namespace Thermi.Linux.Window;

/// <summary>
/// The window and its page in WebKitGTK: the page's files from the install's folder, under the page's policy, on a scheme
/// of thermi's own that WebKit counts as secure; no navigation away, no new window, no permission, no browser menu. The
/// page reaches C# only through <c>window.thermi</c>, which a script set before the page's own answers with the bridge;
/// the answers and the bridge's events are run in the page as WebKit's host must.
/// </summary>
internal sealed class SettingsWindow : IDisposable
{
    private const string Scheme = "thermi";

    private const string Origin = Scheme + "://app/";

    // GTK draws the window's title bar above the page, so the page's title row keeps no room for Windows' caption buttons.
    private const string Caption = ":root { --thermi-caption-width: 0px; }";

    // Beside thermi in the install's folder, as the build copies it.
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "web");

    private readonly Gtk.ApplicationWindow _window;
    private readonly WebKit.WebView _view;
    private readonly IWindowBridge _bridge;
    private readonly Action<WindowTheme> _theme;
    private readonly ILogger _log;
    private readonly TraceEvents? _trace;
    private readonly string? _item;
    private bool _shown;

    // item: the one the window opens on, which the trace says once it shows.
    public SettingsWindow(Gtk.Application app, PageBridge bridge, LinuxHost host, ILogger log, TraceEvents? trace, string? item)
    {
        _bridge = bridge;
        _theme = host.Theme;
        _log = log;
        _trace = trace;
        _item = item;
        _window = Gtk.ApplicationWindow.New(app);
        _window.SetTitle("thermi");
        var first = FirstScreen(host.Screens());
        var size = WindowRoom.First(ThermiConfig.New, [first], []);
        var (least, lowest) = WindowRoom.Minimum(first.Work);
        _window.SetDefaultSize(size.Width, size.Height);
        _window.SetSizeRequest(least, lowest);
        _view = WebKit.WebView.New();
        Lock(_view, trace is not null);
        var content = _view.GetUserContentManager();
        content.AddScript(
            WebKit.UserScript.New(BridgeCalls.Client, WebKit.UserContentInjectedFrames.TopFrame, WebKit.UserScriptInjectionTime.Start, null, null));
        content.AddStyleSheet(WebKit.UserStyleSheet.New(Caption, WebKit.UserContentInjectedFrames.TopFrame, WebKit.UserStyleLevel.User, null, null));
        content.RegisterScriptMessageHandler(BridgeCalls.WebKitHandler, null);
        content.OnScriptMessageReceived += Received;
        _view.OnLoadChanged += (_, e) => Loaded(e.LoadEvent);
        _bridge.Changed += OnChanged;
        _bridge.Readings += OnReadings;
        _bridge.Picked += OnPicked;
        host.Window = _window;
        host.DesktopChanged = bridge.DesktopChanged;
        _window.SetChild(_view);
        _view.LoadUri(Origin + PageFiles.Index);
    }

    /// <summary>The theme Settings chose, or the desktop's while it chose none, which the page's light or dark mode follows.</summary>
    public void Theme(WindowTheme theme) => _theme(theme);

    public void Raise(string? item)
    {
        _window.Present();
        _trace?.Write(TraceEvents.WindowShown, item);
    }

    public void Dispose()
    {
        _bridge.Changed -= OnChanged;
        _bridge.Readings -= OnReadings;
        _bridge.Picked -= OnPicked;
        _view.Dispose();
        _window.Dispose();
    }

    /// <summary>Serves the page's files on thermi's scheme, which WebKit treats as secure, before any page loads.</summary>
    public static void Serve(WebKit.WebContext context)
    {
        context.GetSecurityManager().RegisterUriSchemeAsSecure(Scheme);
        context.RegisterUriScheme(Scheme, Served);
    }

    // The first monitor in GTK's own pixels, which a window's size is given in: Windows' window takes its first size from
    // it, and the desktop places it. A session that says none is taken as one full HD monitor.
    private static Screen FirstScreen(IReadOnlyList<Screen> screens)
    {
        var first = screens.FirstOrDefault(screen => screen.Primary) ?? (screens.Count > 0 ? screens[0] : null);
        return first is null
            ? new Screen("", Primary: true, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), 1)
            : first with { Bounds = Logical(first.Bounds, first.Scale), Work = Logical(first.Work, first.Scale), Scale = 1 };
    }

    private static PixelRect Logical(PixelRect rect, double scale) =>
        new((int)(rect.Left / scale), (int)(rect.Top / scale), (int)(rect.Right / scale), (int)(rect.Bottom / scale));

    // What a person could do to a browser and not to a window: navigate away, open windows, zoom, print, open its menus or
    // tools, or grant a site anything. The tools stay with --trace, to look into a fault.
    private static void Lock(WebKit.WebView view, bool trace)
    {
        var settings = view.GetSettings();
        settings.SetEnableDeveloperExtras(trace);
        settings.SetEnableBackForwardNavigationGestures(false);
        settings.SetJavascriptCanOpenWindowsAutomatically(false);
        settings.SetEnableHtml5Database(false);
        settings.SetEnableHtml5LocalStorage(false);
        settings.SetEnableMediaStream(false);
        settings.SetEnableWebgl(false);
        using var clear = new Gdk.RGBA { Red = 0, Green = 0, Blue = 0, Alpha = 0 };
        view.SetBackgroundColor(clear);
        view.OnDecidePolicy += (_, e) => Refused(e.Decision, e.DecisionType);
        view.OnContextMenu += (_, _) => !trace;
        view.OnPermissionRequest += (_, e) =>
        {
            e.Request.Deny();
            return true;
        };
    }

    private static bool Refused(WebKit.PolicyDecision decision, WebKit.PolicyDecisionType type)
    {
        if (type == WebKit.PolicyDecisionType.Response)
        {
            return false;
        }

        if (type == WebKit.PolicyDecisionType.NavigationAction
            && decision is WebKit.NavigationPolicyDecision navigation
            && navigation.GetNavigationAction().GetRequest().GetUri().StartsWith(Origin, StringComparison.Ordinal))
        {
            return false;
        }

        decision.Ignore();
        return true;
    }

    private static void Served(WebKit.URISchemeRequest request)
    {
        var path = request.GetPath().TrimStart('/');
        path = path.Length == 0 ? PageFiles.Index : path;
        var file = Path.GetFullPath(Path.Combine(Files, path));

        var type = file.StartsWith(Files + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(file) ? PageFiles.TypeOf(path) : null;
        byte[] content = type is null ? [] : File.ReadAllBytes(file);
        using var bytes = GLib.Bytes.New(content);
        using var stream = Gio.MemoryInputStream.NewFromBytes(bytes);
        using var response = WebKit.URISchemeResponse.New(stream, content.Length);
        using var headers = Soup.MessageHeaders.New(Soup.MessageHeadersType.Response);
        headers.Append("Content-Security-Policy", PageFiles.Policy);
        headers.Append("X-Content-Type-Options", "nosniff");
        headers.Append("Cache-Control", "no-store");

        // nosniff reads the type from the headers, not from the response's own, so a style sheet without it never applies.
        if (type is not null)
        {
            headers.Append("Content-Type", type);
        }

        response.SetHttpHeaders(headers);
        if (type is not null)
        {
            response.SetContentType(type);
        }
        else
        {
            response.SetStatus(404, "Not Found");
        }

        request.FinishWithResponse(response);
    }

    // Shown once the page has loaded, so its first frame is already in the window's mode.
    private void Loaded(WebKit.LoadEvent load)
    {
        if (load != WebKit.LoadEvent.Finished || _shown)
        {
            return;
        }

        _shown = true;
        _window.Present();
        _trace?.Write(TraceEvents.WindowShown, _item);
    }

    // The message is let go once read: GirCore warns of one the collector takes while WebKit still holds it.
    private void Received(WebKit.UserContentManager sender, WebKit.UserContentManager.ScriptMessageReceivedSignalArgs e)
    {
        using var message = e.Value;
        _ = AnswerAsync(message.ToJson(0));
    }

    private async Task AnswerAsync(string call)
    {
        string answer;
        try
        {
            answer = await BridgeCalls.AnswerAsync(_bridge, call);
        }
#pragma warning disable CA1031 // Logged, and the page's promise rejected: a fault in one call never takes the window down.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.PageCallFailed(call, ex);
            answer = BridgeCalls.Failed(call, ex.Message);
        }

        await EvaluateAsync(answer);
    }

    private void OnChanged(object? sender, WindowChangeEventArgs change) => Send(nameof(IWindowBridge.Changed), change);

    // A window the desktop hides draws no readings, and reads the latest once it shows.
    private void OnReadings(object? sender, WindowReadingsEventArgs readings)
    {
        if (_window.IsVisible())
        {
            Send(nameof(IWindowBridge.Readings), readings);
        }
    }

    private void OnPicked(object? sender, DesktopPickEventArgs pick) => Send(nameof(IWindowBridge.Picked), pick);

    private void Send(string name, object? value) => Run(BridgeCalls.EventOf(name, value));

    private void Run(string message) => _ = EvaluateAsync(message);

    // What the script returns is let go at once, as a message is.
    private async Task EvaluateAsync(string message)
    {
        try
        {
            using var _ = await _view.EvaluateJavascriptAsync(BridgeCalls.Dispatched(message));
        }
        catch (GLib.GException ex)
        {
            _log.PageThrew(ex.Message);
        }
    }
}
