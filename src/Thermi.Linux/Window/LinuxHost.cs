using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Thermi.App.Logging;
using Thermi.App.Painting;
using Thermi.App.Window;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Updates;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Window;

/// <summary>
/// What the window's bridge asks of Linux, from GTK, the session's bus and the desktop's prompts. Updates are the package
/// manager's. Used on GTK's loop alone.
/// </summary>
internal sealed partial class LinuxHost : IWindowHost, IDisposable
{
    /// <summary>The overlay's id on the session's bus, a GTK application's, whose <c>quit</c> action ends it.</summary>
    public const string Overlay = "io.github.claudiopi.thermi.Overlay";

    private const string OverlayPath = "/io/github/claudiopi/thermi/Overlay";

    private readonly ILogger _log;
    private readonly Gio.DBusConnection? _bus;
    private readonly DesktopPortal _portal;
    private readonly WindowScreens _screens;
    private WindowTheme _theme;
    private ExtensionState? _extension;
    private bool? _signIn;

    public LinuxHost(ILogger log)
    {
        _log = log;
        _bus = Connect();
        _portal = new DesktopPortal(Changed);
        _extension = LinuxFixes.ExtensionState(_bus);
        _screens = new WindowScreens(_bus, Turn.Post, () => DesktopChanged?.Invoke());
    }

    public Gtk.Window? Window { get; set; }

    /// <summary>Does what the page's caption asks, as the window that holds the press it came from answers it.</summary>
    public Action<CaptionAction>? Captioned { get; set; }

    /// <summary>Runs when the desktop changed its scheme, accent or contrast, or the extension came or went.</summary>
    public Action? DesktopChanged { get; set; }

    public HostDesktop Desktop =>
        new(HostOs.Linux, _portal.Accent, _extension, _portal.HighContrast, HostDesktop.BarOf(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")),
            Window?.IsMaximized() ?? false);

    // Linux's packages install the sensors for everyone, and starting them asks through polkit's own prompt.
    public bool ForEveryone => true;

    public bool StartsAtSignIn
    {
        get => _signIn ??= LinuxFixes.StartsAtSignIn();
        set
        {
            LinuxFixes.StartAtSignIn(value);
            _signIn = null;
        }
    }

    /// <summary>The theme Settings chose, or the desktop's scheme while it chose none, which WebKit's page follows.</summary>
    public void Theme(WindowTheme theme)
    {
        _theme = theme;
        if (Gtk.Settings.GetDefault() is { } settings)
        {
            settings.GtkApplicationPreferDarkTheme = theme == WindowTheme.Dark || (theme == WindowTheme.System && _portal.Scheme == true);
        }
    }

    public IReadOnlyList<Screen> Screens() => _screens.Screens;

    public bool LightTaskbar() => DesktopLooks.LightPanels;

    public HighContrastColors? HighContrast() => _portal.HighContrast ? DesktopLooks.Contrasted : null;

    public IOverlayPainter Painter() => new WindowPainter();

    public double? BaseClock() => DesktopLooks.BaseClock();

    // Linux's hotkeys are the desktop's, through the Shell or its portal, and take no key from a layout.
    public IReadOnlySet<int> AltGrKeys() => new HashSet<int>();

    public bool HotkeyHeld(string combination, HotkeysConfig? own) => false;

    // The overlay registers its hotkeys with the desktop, which tells it, not the window, of one it refused.
    public long? HotkeyStatus() => OverlayRunning() ? 0 : null;

    public bool OverlayRunning() =>
        _bus is { } bus && Ask(bus, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", Overlay) is { } owned
        && owned.GetChildValue(0).GetBoolean();

    public void RestHotkeys(bool rest)
    {
        // The overlay's hotkeys are the desktop's own on Linux, which a field of the page does not take while it listens.
    }

    public void Editing(int? overlay, string? selected, string? pointed)
    {
        // The overlay marks what the window edits through its own surface's protocol, which the window does not speak.
    }

    public void ExitThermi()
    {
        if (_bus is { } bus)
        {
            var path = OverlayPath;
            using var _ = Ask(bus, Overlay, path, "org.gtk.Actions", "Activate", "quit");
        }

        Window?.Close();
    }

    public void Caption(CaptionAction action) => Captioned?.Invoke(action);

    public void StartOverlay()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? "thermi") { UseShellExecute = false })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            OverlayNotStarted(_log, ex);
        }
    }

    public void Open(string target, ILogger log)
    {
        var uri = target.Contains("://", StringComparison.Ordinal) ? target : Gio.FileHelper.NewForPath(target).GetUri();
        _ = Launch(Gtk.UriLauncher.New(uri), uri, log);
    }

    public bool Copy(string text)
    {
        if (Gdk.Display.GetDefault()?.GetClipboard() is not { } clipboard)
        {
            return false;
        }

        clipboard.SetText(text);
        return true;
    }

    public async Task<string?> Paste()
    {
        try
        {
            return Gdk.Display.GetDefault()?.GetClipboard() is { } clipboard ? await clipboard.ReadTextAsync() : null;
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    public async Task<string?> SaveFile(string kind, string extension, string name)
    {
        using var dialog = Dialog(kind, extension);
        dialog.SetInitialName(name + extension);
        try
        {
            return (await dialog.SaveAsync(Window))?.GetPath();
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    public async Task<string?> OpenFile(string kind, string extension)
    {
        using var dialog = Dialog(kind, extension);
        try
        {
            return (await dialog.OpenAsync(Window))?.GetPath();
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    // Linux reads the CPU's sensors with the kernel's own drivers.
    public Task<DriverInstall> InstallDriver() => Task.FromResult(DriverInstall.NotInstalled);

    public Task<bool> StartService() => LinuxFixes.StartService();

    public async Task<bool> InstallExtension()
    {
        var installed = await LinuxFixes.InstallExtension(_bus, _extension);
        _extension = LinuxFixes.ExtensionState(_bus);
        return installed;
    }

    public UpdateLine Updates() => new(UpdateLineKind.NotUpdating, Beta: false);

    public void CheckUpdates()
    {
        // The package manager updates thermi with the rest of the system, as the person runs it.
    }

    public void RestartToUpdate()
    {
        // No update waits on thermi: the package manager hands the overlay over to a new version itself.
    }

    // Beside thermi in the install's folder, by its name where a client finds it on the PATH.
    public AgentsView Agents(ValueFormat format, TimeZoneInfo zone) => AgentActivity.View(
        AgentSetup.Command(System.IO.Path.Combine(AppContext.BaseDirectory, AgentSetup.Program), (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')),
        AgentActivity.Changes(AgentsLog.Lines(ThermiLog.AgentsPath)),
        format,
        zone);

    public void Dispose()
    {
        _portal.Dispose();
        _screens.Dispose();
        _bus?.Dispose();
    }

    private static Gtk.FileDialog Dialog(string kind, string extension)
    {
        var filter = Gtk.FileFilter.New();
        filter.SetName(kind);
        filter.AddPattern("*" + extension);
        var filters = Gio.ListStore.New(Gtk.FileFilter.GetGType());
        filters.Append(filter);
        var dialog = Gtk.FileDialog.New();
        dialog.SetFilters(filters);
        dialog.SetDefaultFilter(filter);
        return dialog;
    }

    private static Gio.DBusConnection? Connect()
    {
        try
        {
            return Gio.Functions.BusGetSync(Gio.BusType.Session, null);
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    private static GLib.Variant? Ask(Gio.DBusConnection bus, string name, string path, string face, string method, string argument)
    {
        try
        {
            // org.gtk.Actions' Activate takes the action, its parameters and the platform's data, both empty.
            var parameters = method == "Activate"
                ? GLib.Variant.NewTuple([
                    GLib.Variant.NewString(argument),
                    GLib.Variant.NewArray(GLib.VariantType.New("v"), []),
                    GLib.Variant.NewArray(GLib.VariantType.New("{sv}"), [])])
                : GLib.Variant.NewTuple([GLib.Variant.NewString(argument)]);
            return bus.CallSync(name, path, face, method, parameters, null, Gio.DBusCallFlags.None, 1000, null);
        }
        catch (GLib.GException)
        {
            return null;
        }
    }

    private async Task Launch(Gtk.UriLauncher launcher, string uri, ILogger log)
    {
        try
        {
            await launcher.LaunchAsync(Window);
        }
        catch (GLib.GException ex)
        {
            OpenRefused(log, uri, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The overlay could not be started")]
    private static partial void OverlayNotStarted(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The desktop did not open {Target}")]
    private static partial void OpenRefused(ILogger logger, string target, Exception error);

    // The desktop's look changed: the extension is asked again with it, as a sign-in or the Shell may have changed it.
    private void Changed()
    {
        _extension = LinuxFixes.ExtensionState(_bus);
        Theme(_theme);
        DesktopChanged?.Invoke();
    }
}
