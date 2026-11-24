using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// thermi's menu and the overlay's menu are data in Core, in their order, so the overlay draws Windows' own menus from them.
/// </summary>
public sealed class MenuTests
{
    // Overlays as JSON objects between '|', with ' for ", and words for the rest of the state. An entry is written as its
    // name, then * for the bold default, + for checked and @ for a hotkey beside it; a submenu's entries go in [].
    [Theory]
    [InlineData("{}", "", "Settings* TaskManager HideOverlays ClickThrough Lock Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{}|{}", "", "Settings* TaskManager HideOverlays ClickThrough Lock Overlay[HideThisOverlay ClickThrough Lock] Overlay[HideThisOverlay ClickThrough Lock] Exit", "Settings* TaskManager CopyImage Vertical Lock HideThisOverlay")]
    [InlineData("{}", "flag", "ShowOverlays* Settings TaskManager ClickThrough Lock Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{'hidden':true}", "", "ShowOverlay* Settings TaskManager HideOverlays ClickThrough Lock Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{}|{'hidden':true}", "", "ShowOverlay* Settings TaskManager HideOverlays ClickThrough Lock Overlay[HideThisOverlay ClickThrough Lock] Overlay[ShowOverlay ClickThrough Lock] Exit", "Settings* TaskManager CopyImage Vertical Lock HideThisOverlay")]
    [InlineData("{'hidden':true}|{'hidden':true}", "", "ShowHiddenOverlays* Settings TaskManager HideOverlays ClickThrough Lock Overlay[ShowOverlay ClickThrough Lock] Overlay[ShowOverlay ClickThrough Lock] Exit", "Settings* TaskManager CopyImage Vertical Lock HideThisOverlay")]
    [InlineData("{}", "broken", "SettingsBroken Settings* TaskManager HideOverlays ClickThrough Lock Exit", "SettingsBroken Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{}", "stopped", "StartSensors Settings* TaskManager HideOverlays ClickThrough Lock Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{'clickThrough':true,'locked':true,'style':{'orientation':'vertical'}}", "", "Settings* TaskManager HideOverlays ClickThrough+ Lock+ Exit", "Settings* TaskManager CopyImage Vertical+ Lock+ HideOverlays")]
    [InlineData("{'items':[{'id':'cpu.temperature','metric':'cpu.temperature','stat':'max'}]}", "", "Settings* TaskManager HideOverlays ClickThrough Lock ResetMinMax Exit", "Settings* TaskManager CopyImage Vertical Lock ResetMinMax HideOverlays")]
    [InlineData("{}", "hotkeys", "Settings* TaskManager HideOverlays@ ClickThrough@ Lock ResetMinMax@ Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays@")]
    [InlineData("{}", "gesture10", "Settings* TaskManager HideOverlays ClickThrough Lock Exit", "UndoGesture Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{}", "gesture31", "Settings* TaskManager HideOverlays ClickThrough Lock Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    [InlineData("{'style':{'name':'Night'}}", "looks", "Settings* TaskManager HideOverlays ClickThrough Lock Exit", "Settings* TaskManager CopyImage Look Look+ Look Vertical Lock HideOverlays")]
    [InlineData("{}", "update", "Settings* TaskManager HideOverlays ClickThrough Lock Update Exit", "Settings* TaskManager CopyImage Vertical Lock HideOverlays")]
    public void MenusHaveTheirEntriesInOrder(string overlays, string state, string thermis, string overlaysMenu)
    {
        var words = state.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hotkeys = words.Contains("hotkeys") ? ",'hotkeys':{'toggle':'Ctrl+Alt+H','clickThrough':'Ctrl+Alt+C','resetMinMax':'Ctrl+Alt+R'}" : "";
        var (config, problem) = ThermiConfigJson.Read($"{{'version':2{hotkeys},'overlays':[{overlays.Replace('|', ',')}]}}".Replace('\'', '"'));
        var gesture = words.FirstOrDefault(word => word.StartsWith("gesture", StringComparison.Ordinal)) is { } ago ? TimeSpan.FromSeconds(int.Parse(ago["gesture".Length..], System.Globalization.CultureInfo.InvariantCulture)) : (TimeSpan?)null;
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
    }

    private static string Describe(IReadOnlyList<MenuRow> rows) => string.Join(' ', rows.Select(row => string.Concat(
        row.Entry.ToString(),
        row.Default ? "*" : "",
        row.Checked ? "+" : "",
        row.Hotkey is null ? "" : "@",
        row.Entries.Count == 0 ? "" : $"[{Describe(row.Entries)}]")));
}
