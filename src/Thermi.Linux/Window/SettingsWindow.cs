using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.Window;
using Thermi.Core.Bridge;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Linux.Desktop;

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

    /// <summary>Where the page is served from: a navigation anywhere else is refused.</summary>
    internal const string Origin = Scheme + "://app/";

    // GNOME maximizes a new window that covers more than 80 % of its work area, which GTK does not know: of the whole
    // monitor, 75 % leaves room for a top bar.
    private const double MostOfTheScreen = 0.75;

    // Beside thermi in the install's folder, as the build copies it.
    private static readonly string Files = Path.Combine(AppContext.BaseDirectory, "web");

    private readonly Gtk.ApplicationWindow _window;
    private readonly WebKit.WebView _view;
    private readonly IWindowBridge _bridge;
    private readonly Action<WindowTheme> _theme;
    private readonly ILogger _log;
    private readonly TraceEvents? _trace;
    private readonly string? _item;
    private readonly Gtk.EventControllerLegacy _presses;
    private bool _shown;

    // The last press in the window, which a move from the page's title row starts from, as one from a title bar would. GTK
    // frees the event once dispatched, before the page asks, so only what the move needs of it is kept.
    private (Gdk.Device Device, double X, double Y, uint Time)? _press;

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
        _presses = Gtk.EventControllerLegacy.New();
        Captioned(bridge);
        var first = FirstScreen(host.Screens());
        var size = WindowRoom.First(ThermiConfig.New, [first], [], MostOfTheScreen);
        var (least, lowest) = WindowRoom.Minimum(first.Work);
        _window.SetDefaultSize(size.Width, size.Height);
        _window.SetSizeRequest(least, lowest);
        _view = WebKit.WebView.New();
        Lock(_view, trace is not null, log);
        var content = _view.GetUserContentManager();
        content.AddScript(
            WebKit.UserScript.New(BridgeCalls.Client, WebKit.UserContentInjectedFrames.TopFrame, WebKit.UserScriptInjectionTime.Start, null, null));
        content.RegisterScriptMessageHandler(BridgeCalls.WebKitHandler, null);
        content.OnScriptMessageReceived += Received;
        _view.OnLoadChanged += (_, e) => Loaded(e.LoadEvent);
        _bridge.Changed += OnChanged;
        _bridge.Readings += OnReadings;
        _bridge.Picked += OnPicked;
        host.Window = _window;
        host.DesktopChanged = bridge.DesktopChanged;
        host.Captioned = Caption;
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

    /// <summary>
    /// Comes in front with <paramref name="token"/>, the desktop's for the click on the overlay that asked for it, as a
    /// window its own launcher started does.
    /// </summary>
    public void Front(string token)
    {
        _window.SetStartupId(token);
        _window.Present();
    }

    /// <summary>Tells <paramref name="shown"/> whether the window shows each time it is minimized or shown again.</summary>
    public void Follow(Action<bool> shown) => _window.OnNotify += (_, e) =>
    {
        if (e.Pspec.GetName() == "suspended")
        {
            shown(!_window.IsSuspended());
        }
    };

    public void Dispose()
    {
        _bridge.Changed -= OnChanged;
        _bridge.Readings -= OnReadings;
        _bridge.Picked -= OnPicked;
        MainLoopRelease.Hold(_log, _view, _presses, _window);
        _view.Dispose();
        _presses.Dispose();
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
    private static void Lock(WebKit.WebView view, bool trace, ILogger log)
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
        view.OnContextMenu += (_, e) =>
        {
            // WebKit lets go of the menu once asked: what still holds it is freed on the main loop.
            MainLoopRelease.Hold(log, e.ContextMenu, e.HitTestResult);
            return !trace;
        };
        view.OnPermissionRequest += (_, e) =>
        {
            e.Request.Deny();
            return true;
        };
    }

    /// <summary>
    /// Whether the page may go where it asks: a navigation within thermi's origin, and the answer to one; never a window of
    /// its own, nor anywhere else.
    /// </summary>
    internal static bool Allowed(WebKit.PolicyDecisionType type, string? uri) =>
        type == WebKit.PolicyDecisionType.Response
        || (type == WebKit.PolicyDecisionType.NavigationAction && uri?.StartsWith(Origin, StringComparison.Ordinal) == true);

    /// <summary>
    /// The file of <paramref name="files"/> a request's path asks for, the page itself for none: null for one outside it, as
    /// a path of <c>..</c> asks, or one it does not hold.
    /// </summary>
    internal static string? FileOf(string files, string path)
    {
        var asked = path.TrimStart('/');
        var file = Path.GetFullPath(Path.Combine(files, asked.Length == 0 ? PageFiles.Index : asked));
        return file.StartsWith(files + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(file) ? file : null;
    }

    private static bool Refused(WebKit.PolicyDecision decision, WebKit.PolicyDecisionType type)
    {
        if (Allowed(type, (decision as WebKit.NavigationPolicyDecision)?.GetNavigationAction().GetRequest().GetUri()))
        {
            return false;
        }

        decision.Ignore();
        return true;
    }

    private static void Served(WebKit.URISchemeRequest request)
    {
        var file = FileOf(Files, request.GetPath());
        var type = file is null ? null : PageFiles.TypeOf(file);
        byte[] content = file is not null && type is not null ? File.ReadAllBytes(file) : [];
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

    // A hidden title bar of GTK's own leaves the page's title row the window's caption, as on Windows, while GTK keeps the
    // desktop's shadow and the edges to resize from; the page's caption buttons say whether it is maximized.
    private void Captioned(PageBridge bridge)
    {
        var none = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        none.SetVisible(false);
        _window.SetTitlebar(none);
        _presses.SetPropagationPhase(Gtk.PropagationPhase.Capture);
        _presses.OnEvent += (_, _) => Pressed();
        _window.AddController(_presses);
        _window.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "maximized")
            {
                bridge.DesktopChanged();
            }
        };
    }

    private bool Pressed()
    {
        // GirCore reads no event out of the signal's arguments, so it comes from the controller, and as a plain event.
        var press = _presses.GetCurrentEvent();
        if (press?.GetEventType() == Gdk.EventType.ButtonPress && press.GetDevice() is { } device && press.GetPosition(out var x, out var y))
        {
            _press = (device, x, y, press.GetTime());
        }

        return false;
    }

    // What a title bar does, from the page's own: a move starts from the press that asked for it, in the surface's pixels,
    // as the press has them; a double-click does what the desktop's settings say.
    private void Caption(CaptionAction action)
    {
        var top = _window.GetSurface() as Gdk.Toplevel;
        switch (action)
        {
            case CaptionAction.Move when top is not null && _press is { } press:
                top.BeginMove(press.Device, Gdk.Constants.BUTTON_PRIMARY, press.X, press.Y, press.Time);
                break;
            case CaptionAction.Move:
                break;
            case CaptionAction.TitleDoubleClick:
                TitleClicked(top, Gtk.Settings.GetDefault()?.GtkTitlebarDoubleClick);
                break;
            case CaptionAction.Minimize:
                _window.Minimize();
                break;
            case CaptionAction.Maximize:
                TitleClicked(top, "toggle-maximize");
                break;
            case CaptionAction.Close:
                _window.Close();
                break;
            default:
                break;
        }
    }

    // GTK's names for what a title bar's double-click does, as GNOME's settings choose it.
    private void TitleClicked(Gdk.Toplevel? top, string? does)
    {
        switch (does)
        {
            case "toggle-maximize" when _window.IsMaximized():
                _window.Unmaximize();
                break;
            case "toggle-maximize":
                _window.Maximize();
                break;
            case "minimize":
                _window.Minimize();
                break;
            case "lower":
                top?.Lower();
                break;
            default:
                break;
        }
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
        MainLoopRelease.Hold(_log, message);
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

    // A window minimized, or one the compositor suspended as nobody sees it, draws no readings, as WebView2 pauses a minimized
    // one: WebKit hides its page then, which reads the latest once it shows again.
    private void OnReadings(object? sender, WindowReadingsEventArgs readings)
    {
        if (_window.IsVisible() && _window.GetSurface() is Gdk.Toplevel { } top
            && !top.GetState().HasFlag(Gdk.ToplevelState.Minimized) && !top.GetState().HasFlag(Gdk.ToplevelState.Suspended))
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
