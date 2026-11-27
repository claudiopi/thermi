using Microsoft.Extensions.Time.Testing;
using Thermi.App.Gestures;
using Thermi.Core.Editing;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// thermi's menu and the overlay's menu are data in Core, in their order, so the overlay draws Windows' own menus from them.
/// </summary>
public sealed class MenuTests
{
    private const string Graph = "Graph[Auto+ None Bars Line]";

    private const string Size = "Size[Bigger Smaller | 75 100+ 125 150 200 | SizeCustom]";

    // A 48 px taskbar, where an overlay shows at most 126 %.
    private static readonly Screen Pc = new("main", true, new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 1)
    {
        Taskbar = new PixelRect(0, 1392, 2560, 1440),
    };

    // Overlays as JSON objects between '|', with ' for ", and words for the rest of the state, "on:" naming the item whose
    // pill the overlay's menu opens on. An entry is written as its name, a graph's choice as its graph, then * for the bold
    // default, - for a heading, + for checked and @ for a hotkey beside it; | starts a group and a submenu's entries go in [].
    [Theory] // Rows: each state its step names changes the entries, a gesture's undo has its 30 s boundary, a fix's worn look, a taskbar's cap
    [InlineData(
        "{}",
        "flag",
        "ShowOverlays* | Settings TaskManager CopyImage Vertical ClickThrough Lock | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | TaskManager | ShowOverlays | Exit")]
    [InlineData(
        "{}|{'hidden':true}",
        "",
        "ShowOverlay* | Settings TaskManager ClickThrough Lock HideOverlays | Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideThisOverlay HideOverlays | Exit")]
    [InlineData(
        "{'hidden':true}|{'hidden':true}",
        "",
        "ShowHiddenOverlays* | Settings TaskManager ClickThrough Lock | Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | TaskManager | ShowOverlay | Exit")]
    [InlineData(
        "{}",
        "broken",
        "SettingsBroken | Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"SettingsBroken | Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{}",
        "stopped",
        "StartSensors | Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'clickThrough':true,'locked':true,'style':{'orientation':'vertical'}}",
        "",
        "Settings* TaskManager CopyImage Vertical+ ClickThrough+ Lock+ HideOverlays | Exit",
        $"Settings* | {Graph} Vertical+ {Size} Lock+ | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'items':[{'id':'cpu.temperature','metric':'cpu.temperature','stat':'max'}]}",
        "",
        "Settings* TaskManager CopyImage ClickThrough Lock ResetMinMax HideOverlays | Exit",
        $"Settings* | {Graph} {Size} Lock | CopyImage TaskManager ResetMinMax | HideOverlays | Exit")]
    [InlineData(
        "{}",
        "hotkeys",
        "Settings* TaskManager CopyImage Vertical ClickThrough@ Lock ResetMinMax@ HideOverlays@ | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays@ | Exit")]
    [InlineData(
        "{}",
        "gesture10",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"UndoGesture | Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{}",
        "gesture31",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'style':{'name':'Night'}}",
        "looks",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Look Look+ Look Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    // The look worn is the one the overlay draws as, as the window's tile checks it: Ocean, just saved from it, unnamed.
    [InlineData(
        "{'style':{'gap':2}}",
        "looks",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Look Look Look+ Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    // A desktop whose overlay cannot reach the clipboard offers no copy of it.
    [InlineData(
        "{}",
        "nocopy",
        "Settings* TaskManager Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{}",
        "update",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Update | Exit",
        $"Settings* | {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    // On a pill: of several, on the taskbar or the desktop, it can be separated; not the overlay's only pill, nor while the
    // overlay is hidden or locked, which a hold to lift the pill is not either; once no other overlay fits, grayed with why.
    [InlineData(
        "{}",
        "on:gpu.load",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"EditItem* Settings | Separate {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'position':{'x':100,'y':100}}",
        "on:gpu.load",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"EditItem* Settings | Separate {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'locked':true,'position':{'x':100,'y':100}}",
        "on:gpu.load",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock+ HideOverlays | Exit",
        $"EditItem* Settings | {Graph} Vertical {Size} Lock+ | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'items':[{'id':'cpu.temperature','metric':'cpu.temperature'},{'id':'cpu.load','metric':'cpu.load'}]}",
        "on:cpu.load",
        "Settings* TaskManager CopyImage ClickThrough Lock HideOverlays | Exit",
        $"EditItem* Settings | {Graph} {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{}|{}|{}|{}",
        "on:gpu.load",
        "Settings* TaskManager ClickThrough Lock HideOverlays | Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] | Exit",
        $"EditItem* Settings | Separate-(SeparateFull) {Graph} Vertical {Size} Lock | CopyImage TaskManager | HideThisOverlay HideOverlays | Exit")]
    [InlineData(
        "{'hidden':true}|{}",
        "on:gpu.load",
        "ShowOverlay* | Settings TaskManager ClickThrough Lock HideOverlays | Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] | Exit",
        $"EditItem* Settings | {Graph} Vertical {Size} Lock | TaskManager | ShowOverlay HideOverlays | Exit")]
    // The overlay's graph, while an item it judges follows it, says when some items keep their own.
    [InlineData(
        "{'graph':'line','items':[{'id':'cpu.temperature','metric':'cpu.temperature','graph':'bars'},{'id':'gpu.load','metric':'gpu.load'}]}",
        "",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | Graph[Auto None Bars Line+ | GraphOwn-] Vertical {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    [InlineData(
        "{'items':[{'id':'cpu.temperature','metric':'cpu.temperature','graph':'none'},{'id':'cpu.power','metric':'cpu.power'}]}",
        "",
        "Settings* TaskManager CopyImage ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    // Vertical is offered while it stacks pills, or while it is on, so it can be turned off; one bar of one run stacks nothing.
    [InlineData(
        "{'style':{'layout':'one','orientation':'vertical'},'items':[{'id':'cpu.temperature','metric':'cpu.temperature'}]}",
        "",
        "Settings* TaskManager CopyImage Vertical+ ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Vertical+ {Size} Lock | CopyImage TaskManager | HideOverlays | Exit")]
    // At 200 % on a taskbar that caps it at 126 %, no size would show bigger: the submenu offers what would, and says why.
    [InlineData(
        "{'size':2}",
        "taskbar",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays | Exit",
        $"Settings* | {Graph} Vertical Size[Smaller | 75 100 125 | SizeCustom | SizeOnTaskbar-] Lock | CopyImage TaskManager | HideOverlays | Exit")]
    public void MenusHaveTheirEntriesInOrder(string overlays, string state, string thermis, string overlaysMenu)
    {
        var words = state.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hotkeys = words.Contains("hotkeys") ? ",'hotkeys':{'toggle':'Ctrl+Alt+H','clickThrough':'Ctrl+Alt+C','resetMinMax':'Ctrl+Alt+R'}" : "";
        var (config, problem) = ThermiConfigJson.Read($"{{'version':6{hotkeys},'overlays':[{overlays.Replace('|', ',')}]}}".Replace('\'', '"'));
        var gesture = words.FirstOrDefault(word => word.StartsWith("gesture", StringComparison.Ordinal)) is { } ago
            ? TimeSpan.FromSeconds(int.Parse(ago["gesture".Length..], System.Globalization.CultureInfo.InvariantCulture))
            : (TimeSpan?)null;
        var item = words.FirstOrDefault(word => word.StartsWith("on:", StringComparison.Ordinal))?["on:".Length..];
        var menu = new MenuState(
            HideOverlays: words.Contains("flag"),
            FileBroken: words.Contains("broken"),
            Service: words.Contains("stopped") ? ServiceState.Stopped : ServiceState.Running,
            SinceGesture: gesture,
            SavedLooks: words.Contains("looks") ? [new StyleConfig { Name = "Night" }, new StyleConfig(Gap: 2) { Name = "Ocean" }] : [],
            UpdateReady: words.Contains("update"))
        {
            Screens = words.Contains("taskbar") ? [Pc] : [],
            CanCopy = !words.Contains("nocopy"),
        };

        Assert.Null(problem);
        Assert.Equal(thermis, Describe(Menus.Thermis(config, menu)));
        Assert.Equal(overlaysMenu, Describe(Menus.OfOverlay(config, 0, menu, item)));

        // From an item's tray icon, away from the overlay, the menu says which overlay it acts on and offers click-through.
        var clickThrough = config.Overlays[0].ClickThrough ? "ClickThrough+" : "ClickThrough";
        Assert.Equal(
            "Overlay- | " + overlaysMenu.Replace(" Lock", $" {clickThrough} Lock", StringComparison.Ordinal),
            Describe(Menus.OfOverlay(config, 0, menu, item, fromTray: true)));
        ReadAndChosen(config, menu, item);
    }

    // Every OS's menus read each row with its blanks filled, wherever it shows; and an edit chosen from the overlay's menu
    // changes the settings, but a choice already checked.
    private static void ReadAndChosen(ThermiConfig config, MenuState menu, string? item)
    {
        var numbers = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var (rows, overlay) in new[] { (Menus.Thermis(config, menu), (int?)null), (Menus.OfOverlay(config, 0, menu, item), 0) })
        {
            var place = new MenuPlace(config, Texts.English, numbers, overlay) { UndoText = Texts.English.Of(OverlayText.UndoLock) };
            foreach (var (row, submenu) in rows.SelectMany(row => row.Entries.Select(entry => (entry, true)).Prepend((row, false))))
            {
                var said = MenuTexts.TextOf(row, place, submenu) + MenuTexts.Beside(row, Texts.English);
                Assert.False(string.IsNullOrWhiteSpace(said) || said.Contains('{', StringComparison.Ordinal), $"{row.Entry} reads '{said}'");
                var choice = row.Entry is MenuEntry.Graph or MenuEntry.Look or MenuEntry.Size;
                var on = MenuEdits.On(submenu && overlay is null ? row.Overlay : overlay, config.Overlays.Count);
                if (overlay is not null && MenuEdits.EditOf(row, on, 0, menu.SavedLooks) is { } edit && !(choice && row.Checked))
                {
                    Assert.NotEqual(ThermiConfigJson.Write(config), ThermiConfigJson.Write(edit(config)));
                }
            }
        }
    }

    // The menu offers to take back a lock chosen from it only while the settings still hold it: after the window's Ctrl+Z
    // took it back the entry would do nothing, while an edit made elsewhere since leaves it to do.
    [Theory] // Rows: the settings as the lock left them, taken back by the window, and changed elsewhere since
    [InlineData("locked", true)]
    [InlineData("undone", false)]
    [InlineData("other", true)]
    public void AMenuEditsUndoIsOfferedOnlyWhileItWouldChangeTheSettings(string now, bool offered)
    {
        var before = new ThermiConfig();
        var after = OverlayEdits.SetLocked(before, 0, true).Config;
        var undo = new GestureUndo(new FakeTimeProvider(), _ => { });
        undo.Add(GestureKind.Menu, before, after, OverlayText.UndoLock);
        var settings = now switch
        {
            "locked" => after,
            "undone" => before,
            _ => OverlayEdits.SetClickThrough(after, 0, true).Config,
        };

        Assert.Equal(offered ? OverlayText.UndoLock : null, undo.Offered(settings));
    }

    // Each entry of one menu gets its own key, from the start of a word first, whatever its case or language, and an
    // entry without a letter or a digit left gets none, nor a grayed one, written after "-", which nothing chooses; an
    // "&" of the text stays one.
    [Theory] // Rows: each rule of the keys its step states: a word's start, any case or language, none left, a grayed note, an "&"
    [InlineData("Alpha one|Alpha two|beta|Ölfarbe|öl", "&Alpha one|Alpha &two|&beta|&Ölfarbe|ö&l")]
    [InlineData("R&D|Rd|…", "&R&&D|R&d|…")]
    [InlineData("ab|ba|ab", "&ab|&ba|ab")]
    [InlineData("Line|-Some & own|Settings", "&Line|Some && own|&Settings")]
    public void EachEntryOfAMenuHasItsOwnAccessKey(string texts, string marked)
    {
        var entries = texts.Split('|');
        var grayed = entries.Select(entry => entry.StartsWith('-')).ToArray();
        Assert.Equal(marked, string.Join('|', AccessKeys.Mark([.. entries.Select(entry => entry.TrimStart('-'))], grayed)));
    }

    private static string Describe(IReadOnlyList<MenuRow> rows) => string.Join(' ', rows.Select((row, at) => string.Concat(
        row.Starts && at > 0 ? "| " : "",
        row.Graph?.ToString() ?? (row is { Entry: MenuEntry.Size, Size: { } size } ? Percent(size) : row.Entry.ToString()),
        Marks(row),
        row.Entries.Count == 0 ? "" : $"[{Describe(row.Entries)}]")));

    private static string Percent(double size) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size * 100:0}");

    private static string Marks(MenuRow row) => string.Concat(
        row.Default ? "*" : "",
        row.Heading ? "-" : "",
        row.Checked ? "+" : "",
        row.Hotkey is null ? "" : "@",
        row.Note is { } note ? $"({note})" : "");
}
