using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// thermi's menu and the overlay's menu are data in Core, in their order, so the overlay draws Windows' own menus from them.
/// </summary>
public sealed class MenuTests
{
    // Overlays as JSON objects between '|', with ' for ", and words for the rest of the state. An entry is written as its
    // name, then * for the bold default, - for a heading, + for checked and @ for a hotkey beside it; a submenu's entries go in [].
    [Theory]
    [InlineData(
        "{}",
        "",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{}|{}",
        "",
        "Settings* TaskManager ClickThrough Lock HideOverlays Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideThisOverlay Exit")]
    [InlineData(
        "{}",
        "flag",
        "ShowOverlays* Settings TaskManager CopyImage Vertical ClickThrough Lock Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{'hidden':true}",
        "",
        "ShowOverlay* Settings TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{}|{'hidden':true}",
        "",
        "ShowOverlay* Settings TaskManager ClickThrough Lock HideOverlays Overlay[CopyImage Vertical ClickThrough Lock HideThisOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideThisOverlay Exit")]
    [InlineData(
        "{'hidden':true}|{'hidden':true}",
        "",
        "ShowHiddenOverlays* Settings TaskManager ClickThrough Lock HideOverlays Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] "
            + "Overlay[CopyImage Vertical ClickThrough Lock ShowOverlay] Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideThisOverlay Exit")]
    [InlineData(
        "{}",
        "broken",
        "SettingsBroken Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "SettingsBroken Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{}",
        "stopped",
        "StartSensors Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{'clickThrough':true,'locked':true,'style':{'orientation':'vertical'}}",
        "",
        "Settings* TaskManager CopyImage Vertical+ ClickThrough+ Lock+ HideOverlays Exit",
        "Settings* TaskManager CopyImage Vertical+ Lock+ HideOverlays Exit")]
    [InlineData(
        "{'items':[{'id':'cpu.temperature','metric':'cpu.temperature','stat':'max'}]}",
        "",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock ResetMinMax HideOverlays Exit",
        "Settings* TaskManager CopyImage Vertical Lock ResetMinMax HideOverlays Exit")]
    [InlineData(
        "{}",
        "hotkeys",
        "Settings* TaskManager CopyImage Vertical ClickThrough@ Lock ResetMinMax@ HideOverlays@ Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays@ Exit")]
    [InlineData(
        "{}",
        "gesture10",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "UndoGesture Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{}",
        "gesture31",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{'style':{'name':'Night'}}",
        "looks",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Exit",
        "Settings* TaskManager CopyImage Look Look+ Look Vertical Lock HideOverlays Exit")]
    [InlineData(
        "{}",
        "update",
        "Settings* TaskManager CopyImage Vertical ClickThrough Lock HideOverlays Update Exit",
        "Settings* TaskManager CopyImage Vertical Lock HideOverlays Exit")]
    public void MenusHaveTheirEntriesInOrder(string overlays, string state, string thermis, string overlaysMenu)
    {
        var words = state.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hotkeys = words.Contains("hotkeys") ? ",'hotkeys':{'toggle':'Ctrl+Alt+H','clickThrough':'Ctrl+Alt+C','resetMinMax':'Ctrl+Alt+R'}" : "";
        var (config, problem) = ThermiConfigJson.Read($"{{'version':2{hotkeys},'overlays':[{overlays.Replace('|', ',')}]}}".Replace('\'', '"'));
        var gesture = words.FirstOrDefault(word => word.StartsWith("gesture", StringComparison.Ordinal)) is { } ago
            ? TimeSpan.FromSeconds(int.Parse(ago["gesture".Length..], System.Globalization.CultureInfo.InvariantCulture))
            : (TimeSpan?)null;
        var menu = new MenuState(
            HideOverlays: words.Contains("flag"),
            FileBroken: words.Contains("broken"),
            Service: words.Contains("stopped") ? ServiceState.Stopped : ServiceState.Running,
            SinceGesture: gesture,
            SavedLooks: words.Contains("looks") ? ["Night", "Ocean"] : [],
            UpdateReady: words.Contains("update"));

        Assert.Null(problem);
        Assert.Equal(thermis, Describe(Menus.Thermis(config, menu)));
        Assert.Equal(overlaysMenu, Describe(Menus.OfOverlay(config, 0, menu)));

        // From an item's tray icon, away from the overlay, the menu says which overlay it acts on and offers click-through.
        var clickThrough = config.Overlays[0].ClickThrough ? "ClickThrough+" : "ClickThrough";
        Assert.Equal(
            "Overlay- " + overlaysMenu.Replace(" Lock", $" {clickThrough} Lock", StringComparison.Ordinal),
            Describe(Menus.OfOverlay(config, 0, menu, fromTray: true)));
    }

    private static string Describe(IReadOnlyList<MenuRow> rows) => string.Join(' ', rows.Select(row => string.Concat(
        row.Entry.ToString(),
        row.Default ? "*" : "",
        row.Heading ? "-" : "",
        row.Checked ? "+" : "",
        row.Hotkey is null ? "" : "@",
        row.Entries.Count == 0 ? "" : $"[{Describe(row.Entries)}]")));
}
