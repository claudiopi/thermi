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
using Thermi.Linux.Agents;
using Thermi.Linux.Desktop;

namespace Thermi.Linux.Window;

/// <summary>
/// What the window's bridge asks of Linux, from GTK, the session's bus and the desktop's prompts. Updates are the package
/// manager's. Used on GTK's loop alone.
/// </summary>
internal sealed partial class LinuxHost : IWindowHost, IDisposable
{
    private readonly ILogger _log;
    private readonly DesktopPortal _portal;
    private readonly UserUnit _signInUnit = new();
    private readonly Gnome.ShellLink? _link;
    private readonly WindowScreens _screens;
    private readonly OverlayLink _overlay;
    private WindowTheme _theme;
    private ExtensionState? _extension;

    // The Shell did not install the extension whose files are in place: only the next sign-in shows the overlay.
    private bool _installRefused;
    private bool? _signIn;

    public LinuxHost(ILogger log)
    {
        _log = log;
        var bus = Connect();
        _portal = new DesktopPortal(bus, Changed, log);
        _link = bus is null ? null : new Gnome.ShellLink(bus, log) { StateChanged = () => Turn.Post(Changed) };
        _extension = ExtensionOf();
        _screens = new WindowScreens(bus, _link, Turn.Post, () => DesktopChanged?.Invoke());
        _overlay = new OverlayLink(bus, () => DesktopChanged?.Invoke());
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
        get => _signIn ??= _signInUnit.On;
        set
        {
            try
            {
                _signInUnit.Set(value);
            }
            finally
            {
                _signIn = null;
            }
        }
    }

    /// <summary>
    /// The theme Settings chose, or the desktop's scheme while it chose none or the desktop asks for high contrast, which
    /// WebKit's page follows; a desktop that prefers no scheme is light, as GNOME's default.
    /// </summary>
    public void Theme(WindowTheme theme)
    {
        _theme = theme;
        if (Gtk.Settings.GetDefault() is { } settings)
        {
            settings.GtkApplicationPreferDarkTheme = WindowThemes.Dark(theme, lightApps: _portal.Scheme != true, _portal.HighContrast);
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

    public bool OverlayRunning() => _overlay.Running;

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
        _overlay.Quit();
        Window?.Close();
    }

    public void Caption(CaptionAction action) => Captioned?.Invoke(action);

    // As a systemd user unit, which ends it with the session; thermi.service also restarts it if it fails.
    public async Task<bool> StartOverlay()
    {
        var started = await LinuxFixes.StartOverlay();
        if (!started)
        {
            OverlayNotStarted(_log);
        }

        return started;
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

    /// <summary>
    /// Installs the extension behind GNOME's prompt, turns it on, or asks the Shell for its update, which it applies at the
    /// next sign-in: true once it runs, or the update was asked; false when the person declined or the Shell failed.
    /// </summary>
    public async Task<bool> InstallExtension()
    {
        // Read by the Shell only at the next sign-in, the extension is the person's to apply.
        if (_link is null || _extension is not { } fix || fix == ExtensionState.NextSignIn)
        {
            return false;
        }

        try
        {
            using var answer = await (fix switch
            {
                ExtensionState.Missing => _link.InstallAsync(),
                ExtensionState.Disabled => _link.EnableAsync(),
                ExtensionState.OtherVersion or ExtensionState.Enabled or ExtensionState.NextSignIn => _link.CheckForUpdatesAsync(),
                _ => _link.CheckForUpdatesAsync(),
            });
        }
        catch (GLib.GException)
        {
            // Declined, offline, or the prompt outlasted the call: what the Shell has now says which.
        }

        _installRefused = fix == ExtensionState.Missing && _link.State() == Gnome.ExtensionState.Unread;
        _extension = ExtensionOf();
        return fix == ExtensionState.OtherVersion || _extension == ExtensionState.Enabled;
    }

    public UpdateLine Updates() => LinuxAbout.Updates();

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
        _link?.Dispose();
        _overlay.Dispose();
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

    // The session's one connection, which GirCore hands out as one object to every part of the process: it stays open.
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Neither the overlay's user unit nor a unit of its own started")]
    private static partial void OverlayNotStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The desktop did not open {Target}")]
    private static partial void OpenRefused(ILogger logger, string target, Exception error);

    private ExtensionState? ExtensionOf() =>
        _link is null || !(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase) ? null
        : _link.State() switch
        {
            Gnome.ExtensionState.Missing => ExtensionState.Missing,
            Gnome.ExtensionState.Unread => _installRefused ? ExtensionState.NextSignIn : ExtensionState.Missing,
            Gnome.ExtensionState.Off => ExtensionState.Disabled,
            Gnome.ExtensionState.OtherVersion => ExtensionState.OtherVersion,
            Gnome.ExtensionState.Ours or Gnome.ExtensionState.Starting => ExtensionState.Enabled,
            _ => null,
        };

    // The desktop's look or the extension changed: the extension is asked again, as a sign-in or the Shell may have changed it.
    private void Changed()
    {
        _extension = ExtensionOf();
        Theme(_theme);
        DesktopChanged?.Invoke();
    }
}
