using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Thermi.App.Link;
using Thermi.App.Painting;
using Thermi.App.Window;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Updates;
using Thermi.Windows.Install;
using Thermi.Windows.Logging;
using Thermi.Windows.Overlay;
using Thermi.Windows.Painting;
using Thermi.Windows.Ui.Views;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;
using Thermi.Windows.Win32.Clipboard;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Ui.Web;

/// <summary>
/// What the window's bridge asks of Windows: GDI+'s painter, the registry's mode, the overlay's hotkeys and its link,
/// the clipboard and the common dialogs over the window, the Run key, the driver and the service through UAC, and the
/// updates. Used on the window's thread alone.
/// </summary>
internal sealed class WindowsHost(WindowStart start) : IWindowHost
{
    private readonly WindowFixes _fixes = new(start.Environment, start.Time);
    private readonly WindowUpdates _updates = new(start.Time, start.Environment.Log);

    public WindowEnd? Link { get; set; }

    /// <summary>The window: the dialogs show over it, and "Exit thermi" closes it.</summary>
    public HWND Window { get; set; }

    public HostDesktop Desktop => HostDesktop.Windows;

    public bool ForEveryone => SensorPipes.InstallsForEveryone;

    public Task<bool> StartsAtSignIn() => Task.FromResult(start.Environment.Startup.On);

    public Task StartAtSignIn(bool on)
    {
        start.Environment.Startup.Set(on);
        return Task.CompletedTask;
    }

    public IReadOnlyList<Screen> Screens() => start.Environment.Screens();

    public bool LightTaskbar() => Win32.Desktop.LightTaskbar();

    public HighContrastColors? HighContrast() => Win32.Desktop.HighContrast();

    public IOverlayPainter Painter() => new Painter();

    public double? BaseClock() => CpuWatch.ReadBaseClock();

    public IReadOnlySet<int> AltGrKeys() => start.Environment.AltGrKeys();

    public bool HotkeyHeld(string combination, HotkeysConfig? own) => HotkeyRegistry.HeldElsewhere(combination, own);

    public long? HotkeyStatus() => start.Environment.HotkeyStatus();

    public bool OverlayRunning() => OverlayHost.Running();

    public void RestHotkeys(bool rest) => start.Environment.RestHotkeys(rest);

    public void Editing(int? overlay, string? selected, string? pointed) => Link?.Editing(overlay, selected, pointed);

    /// <summary>The overlay is asked to close, as its tray's "Exit thermi" closes it, and then the window closes.</summary>
    public void ExitThermi()
    {
        var host = PInvoke.FindWindowEx(HWND.Null, HWND.Null, HiddenWindow.HostClass, null);
        if (!host.IsNull)
        {
            PInvoke.PostMessage(host, PInvoke.WM_CLOSE, 0, 0);
        }

        PInvoke.PostMessage(Window, PInvoke.WM_CLOSE, 0, 0);
    }

    public Task<bool> StartOverlay()
    {
        try
        {
            Program.Start(start.Environment.ExePath, start.Environment.Log.IsEnabled(LogLevel.Debug));
            return Task.FromResult(true);
        }
        catch (Win32Exception ex)
        {
            start.Environment.Log.OverlayStartFailed(ex);
            return Task.FromResult(false);
        }
    }

    public void Open(string target, ILogger log) => WindowLinks.OpenUrl(target, log);

    public bool Copy(string text) => ClipboardData.WriteText(Window, text);

    public Task<string?> Paste() => Task.FromResult(ClipboardText.Read(Window));

    public Task<string?> SaveFile(string kind, string extension, string name) => Task.FromResult(FileDialogs.Save(Window, kind, extension, name));

    public Task<string?> OpenFile(string kind, string extension) => Task.FromResult(FileDialogs.Open(Window, kind, extension));

    public Task<DriverInstall> InstallDriver() => _fixes.InstallDriver();

    public Task<bool> StartService() => _fixes.StartService();

    // Windows' overlay needs no extension.
    public Task<bool> InstallExtension() => Task.FromResult(false);

    public void Caption(CaptionAction action)
    {
        // Windows draws and answers the caption itself, and the page draws none to ask with.
    }

    public UpdateLine Updates() => _updates.Line();

    public void CheckUpdates() => _updates.Ask();

    public void RestartToUpdate()
    {
        if (_updates.Restart())
        {
            PInvoke.PostMessage(Window, PInvoke.WM_CLOSE, 0, 0);
        }
    }

    public AgentsView Agents(ValueFormat format, TimeZoneInfo zone) => WindowAgents.View(format, zone);
}
