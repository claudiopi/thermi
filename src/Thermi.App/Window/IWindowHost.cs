using Microsoft.Extensions.Logging;
using Thermi.App.Painting;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Look;
using Thermi.Core.Pipe;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Updates;

namespace Thermi.App.Window;

/// <summary>
/// What the window's bridge asks of its OS, which each host answers its own way: the monitors and the desktop's mode, the
/// painter, the overlay's hotkeys and its link, the clipboard and the file dialogs, starting at sign-in, the fixes, and the
/// updates. <see cref="PageBridge"/> decides the rest alike on every OS. Used on the window's thread alone.
/// </summary>
internal interface IWindowHost
{
    /// <summary>The OS, and what its desktop says now, which picks the fixes and texts the page shows.</summary>
    HostDesktop Desktop { get; }

    /// <summary>Whether the sensors were installed for this user alone, whose fixes install them for everyone.</summary>
    bool ForEveryone { get; }

    /// <summary>The monitors, their taskbars and work areas, as the desktop has them now.</summary>
    IReadOnlyList<Screen> Screens();

    /// <summary>Whether the taskbar, or the desktop's panel, is light, which the overlay's default look follows.</summary>
    bool LightTaskbar();

    /// <summary>The desktop's high contrast colors, null while it has none.</summary>
    HighContrastColors? HighContrast();

    /// <summary>A new painter of the overlay, as its own window paints it on this OS.</summary>
    IOverlayPainter Painter();

    /// <summary>The CPU's base clock in MHz, which a clock's meter reads against, null where the OS does not say.</summary>
    double? BaseClock();

    /// <summary>The keys AltGr types a character with on the keyboard layout now, which no Ctrl+Alt hotkey may take.</summary>
    IReadOnlySet<int> AltGrKeys();

    /// <summary>Whether another program holds <paramref name="combination"/>, which thermi's own <paramref name="own"/> do not.</summary>
    bool HotkeyHeld(string combination, HotkeysConfig? own);

    /// <summary>Asks the overlay which hotkeys it could not take, null when none answers. Called off the window's thread.</summary>
    long? HotkeyStatus();

    /// <summary>Whether the overlay runs now.</summary>
    bool OverlayRunning();

    /// <summary>Asks the overlay to let its hotkeys rest, or take them again.</summary>
    void RestHotkeys(bool rest);

    /// <summary>Tells the overlay what the page edits, so it marks it.</summary>
    void Editing(int? overlay, string? selected, string? pointed);

    /// <summary>Whether thermi starts at sign-in, as "Start with Windows" or "Start at sign-in" sets it.</summary>
    bool StartsAtSignIn { get; set; }

    /// <summary>Asks the overlay to close, and then closes the window.</summary>
    void ExitThermi();

    /// <summary>Starts the overlay, which the window's process never is.</summary>
    void StartOverlay();

    /// <summary>Opens a file or a link in the desktop's own program for it; one it cannot open is logged.</summary>
    void Open(string target, ILogger log);

    /// <summary>Puts <paramref name="text"/> on the clipboard: false when the desktop refused it.</summary>
    bool Copy(string text);

    /// <summary>The clipboard's text, null when it holds none or the desktop refused it.</summary>
    Task<string?> Paste();

    /// <summary>The path a file is saved to, from the desktop's own dialog over the window: null when cancelled.</summary>
    Task<string?> SaveFile(string kind, string extension, string name);

    /// <summary>The path of the file picked in the desktop's own dialog over the window: null when cancelled.</summary>
    Task<string?> OpenFile(string kind, string extension);

    /// <summary>The fixes of a CPU sensor that reads nothing without the driver, where the OS has one.</summary>
    Task<DriverInstall> InstallDriver();

    /// <summary>Starts the stopped sensor service, behind the desktop's own prompt for administrator rights.</summary>
    Task<bool> StartService();

    /// <summary>Installs or turns on the desktop's extension the overlay needs, where it needs one.</summary>
    Task<bool> InstallExtension();

    /// <summary>What About says of thermi's updates.</summary>
    UpdateLine Updates();

    /// <summary>Looks for an update now.</summary>
    void CheckUpdates();

    /// <summary>Restarts thermi into the update that waits, and closes the window, when one waits.</summary>
    void RestartToUpdate();

    /// <summary>The AI agents section: where thermi-cli is, the PATH, and the agents' log.</summary>
    AgentsView Agents(ValueFormat format, TimeZoneInfo zone);
}

/// <summary>
/// What the window opens with on any OS: the overlay asked for, counted from 1 or 0 for none, its texts, the clock, the
/// sensors, the settings file, the log, and the fan finder's load, which tests replace so they never load the CPU.
/// </summary>
internal sealed record PageStart(int Overlay, Texts Texts, TimeProvider Time, ISensorFeed Sensors, string SettingsPath, ILogger Log)
{
    /// <summary>Loads the CPU for the fan finder until cancelled.</summary>
    public required Action<CancellationToken> Load { get; init; }

    /// <summary>True with <c>--pseudo</c>, whose texts stay whatever language the settings choose.</summary>
    public bool Pseudo { get; init; }

    /// <summary>The item of <see cref="Overlay"/> the window opens on, as a double-click on a value asks, or null for the overlay.</summary>
    public string? Item { get; init; }

    /// <summary>True with <c>--size</c>: the window opens on the overlay's Size row.</summary>
    public bool SizeRow { get; init; }
}
