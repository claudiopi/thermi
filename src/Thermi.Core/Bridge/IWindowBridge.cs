using Thermi.Core.Search;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;

namespace Thermi.Core.Bridge;

/// <summary>
/// Everything the settings window's page asks of C#, and the only way it reaches it
/// (<c>docs/adr/0007-settings-window-in-webview2-and-react.md</c>). The page's TypeScript for it is generated from this
/// interface in the build, so a change that breaks either side fails it; the page holds no rule, and every answer is
/// C#'s. An overlay is counted from 0, as the settings list them; a refusal is its text's key, like "Refusal.NotFound".
/// </summary>
public interface IWindowBridge
{
    /// <summary>
    /// Raised when anything the page reads may have changed: the settings, the service, the finder, the desktop; with
    /// <see cref="WindowChangeEventArgs.Sensors"/> when the sensors or the service may have.
    /// </summary>
    event EventHandler<WindowChangeEventArgs>? Changed;

    /// <summary>
    /// Raised for each second's new readings that changed nothing else, with the view and the status they give, so the
    /// page reads again only what reads the readings, as a chart.
    /// </summary>
    event EventHandler<WindowReadingsEventArgs>? Readings;

    /// <summary>A value or an overlay's edge clicked or pointed at on a real overlay.</summary>
    event EventHandler<DesktopPickEventArgs>? Picked;

    Task<ThermiConfig> Settings();

    /// <summary>Why the settings file cannot be used, or null when it can; while it cannot, every edit is refused.</summary>
    Task<ConfigProblemKind?> Problem();

    /// <summary>Applies an edit through Core's rules, as one undo step: null when it was made, else the refusal's key.</summary>
    Task<string?> Edit(PageEdit edit);

    /// <summary>From true to false, as a drag from press to release, the edits are one undo step.</summary>
    Task Gesture(bool active);

    /// <summary>What Core decides the overlays show now: their pills, each item's value and level, and each sensor's value.</summary>
    Task<WindowView> View();

    /// <summary>An item's chart, or null when its meter has none.</summary>
    Task<ChartView?> Chart(string id);

    /// <summary>Ends thermi: the overlay, and then the window.</summary>
    Task ExitThermi();

    /// <summary>Undoes the last step: what it changed back, or null when there was none.</summary>
    Task<Undone?> Undo();

    Task<Undone?> Redo();

    /// <summary>What the window shows beside the settings: the overlay, the driver, the hotkeys and the file's problem.</summary>
    Task<WindowStatus> Status();

    Task<UndoSteps> Steps();

    Task<About> About();

    /// <summary>Opens the settings file in its editor, or one of About's pages in the browser.</summary>
    Task Open(Opened what);

    /// <summary>Has the overlay wear the look file whose text Windows' clipboard holds: null when worn, else the refusal's key.</summary>
    Task<string?> PasteLook(int overlay);

    Task<SensorView> Snapshot();

    /// <summary>An item's meter over the last ten minutes, a value a second, oldest first; null for a gap.</summary>
    Task<IReadOnlyList<double?>> History(string id);

    /// <summary>Every text of the window's language, keyed "Enum.Member".</summary>
    Task<IReadOnlyDictionary<string, string>> Texts();

    Task<IReadOnlyList<MonitorView>> Monitors();

    /// <summary>An error the page threw and did not handle, with its stack, which goes to thermi's log.</summary>
    Task Report(string error);

    /// <summary>The overlays, by index, that as they show do not fit the taskbar's free room beside the app buttons.</summary>
    Task<IReadOnlyList<int>> Crowded();

    /// <summary>The taskbar spots where an overlay, as it shows, would have no room beside the app buttons, which the map marks.</summary>
    Task<IReadOnlyList<SnapConfig>> Roomless(int overlay);

    /// <summary>The saved looks by their names.</summary>
    Task<IReadOnlyDictionary<string, StyleConfig>> Looks();

    Task<string?> SaveLook(int overlay, string name);

    Task<string?> RenameLook(string name, string to);

    Task<string?> DeleteLook(string name);

    Task ExportLook(int overlay);

    /// <summary>Exports a saved look by its name, from its "…", whichever overlay wears it.</summary>
    Task ExportSavedLook(string name);

    /// <summary>
    /// Has the overlay wear a look file's text, dropped or pasted, or with none the file picked in Windows' dialog, as one
    /// undo step: null when it was worn or no file was picked, else the refusal's key.
    /// </summary>
    Task<string?> ImportLook(int overlay, string? text);

    /// <summary>
    /// The catalog: every sensor the service reads freshly, grouped by device, filtered by the words of
    /// <paramref name="search"/> and by <paramref name="kinds"/>, any when empty; and unless <paramref name="all"/>, without
    /// those nobody would add, which it counts.
    /// </summary>
    Task<CatalogView> Catalog(string search, IReadOnlyList<SensorKind> kinds, bool all);

    /// <summary>
    /// The window's search over a list the page already has, as a dropdown's choices: those every word of
    /// <paramref name="query"/> finds, best first, each with what matched, as the catalog finds its sensors.
    /// </summary>
    Task<IReadOnlyList<Found>> Search(string query, IReadOnlyList<Findable> among);

    Task FindCpuFan();

    /// <summary>Stops a run, or once it is done, puts its result away.</summary>
    Task CancelFind();

    Task<FinderView> Finder();

    /// <summary>Installs thermi's driver behind Windows' prompt, and says how it ended.</summary>
    Task<DriverInstall> InstallDriver();

    /// <summary>Starts the sensor service behind Windows' prompt: false when the prompt was refused.</summary>
    Task<bool> StartService();

    Task StartOverlay();

    /// <summary>Keeps the broken settings file beside it and writes the last settings read well.</summary>
    Task Restore();

    Task<bool> StartWithWindows();

    Task SetStartWithWindows(bool on);

    /// <summary>Whether another program holds the combination as its hotkey.</summary>
    Task<bool> HotkeyHeld(string combination);

    /// <summary>Lets the overlay's hotkeys rest while a hotkey box listens, so the keys reach the page.</summary>
    Task RestHotkeys(bool rest);

    /// <summary>The overlay the page edits, its selected item and the item pointed at, each or null, which the real overlay marks.</summary>
    Task Editing(int? overlay, string? selected, string? pointed);

    /// <summary>
    /// What the window was opened on, an overlay and maybe its item, or null for neither; asked once by the page as it
    /// starts, since a <see cref="Picked"/> raised before it listens never reaches it.
    /// </summary>
    Task<DesktopPickEventArgs?> Start();
}
