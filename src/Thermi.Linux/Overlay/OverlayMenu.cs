using System.Diagnostics;
using Thermi.App.SettingsFile;
using Thermi.Cli.Commands;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Overlay;

/// <summary>
/// An overlay's menu as the Shell's own popup menu: Core's rows, each with the text every OS's menu gives it, flattened
/// with the row each sits under, and the row chosen done as Windows' menu does it.
/// </summary>
internal sealed class OverlayMenu(OverlayParts parts, Action exit)
{
    // The system monitors GNOME, KDE and the newer GNOME apps ship, the first found opening.
    private static readonly string[] TaskManagers = ["gnome-system-monitor", "plasma-systemmonitor", "resources"];

    private IReadOnlyList<MenuRow> _shown = [];
    private int _overlay;

    private Settings Settings => parts.Settings;

    /// <summary>Shows overlay <paramref name="overlay"/>'s menu at a point of the stage, on <paramref name="item"/> or its edge.</summary>
    public void Show(int overlay, double x, double y, string? item, ServiceState service, Snapshot? known)
    {
        var config = Settings.Config;
        var undo = parts.Gestures.Offered(config);
        var state = new MenuState(parts.Visibility.HideOverlays, Settings.Problem is not null, service, undo is null ? null : TimeSpan.Zero, Looks(), false);
        var texts = Texts.For(config.Language, System.Globalization.CultureInfo.CurrentUICulture);
        var place = new MenuPlace(config, texts, parts.Culture, overlay) { UndoText = undo is { } offered ? texts.Of(offered) : null, Known = known };
        var all = new List<MenuRow>();
        var rows = new List<ShellRow>();
        Flatten(Menus.OfOverlay(config, overlay, state, item), -1, place, all, rows);
        (_shown, _overlay) = (all, overlay);
        parts.Call(parts.Link.Menu((uint)overlay, x, y, rows));
    }

    /// <summary>The row at <paramref name="index"/> of the menu last shown was chosen.</summary>
    public void Chosen(int index)
    {
        if (index >= 0 && index < _shown.Count)
        {
            Choose(_shown[index], _overlay);
        }
    }

    private static void Flatten(IReadOnlyList<MenuRow> rows, int parent, MenuPlace place, List<MenuRow> all, List<ShellRow> shown)
    {
        foreach (var row in rows)
        {
            all.Add(row);
            var text = MenuTexts.TextOf(row, place, parent >= 0);
            shown.Add(new ShellRow(parent, text, MenuTexts.Beside(row, place.Texts), row.Checked, row.Heading, row.Starts));
            if (row.Entries.Count > 0)
            {
                Flatten(row.Entries, all.Count - 1, place with { Overlay = row.Overlay ?? place.Overlay }, all, shown);
            }
        }
    }

    private IReadOnlyList<StyleConfig> Looks() => new SavedStyles(ConfigStore.StylesFolder(Settings.FilePath)).List();

    private void Choose(MenuRow row, int menuOverlay)
    {
        var target = row.Overlay ?? menuOverlay;
        switch (row.Entry)
        {
            case MenuEntry.SettingsBroken or MenuEntry.Settings or MenuEntry.EditItem or MenuEntry.SizeCustom:
                OpenSettings(target, row.Item);
                break;
            case MenuEntry.TaskManager:
                Start(TaskManagers.FirstOrDefault(Installed) ?? TaskManagers[0], []);
                break;
            case MenuEntry.ShowOverlays or MenuEntry.HideOverlays:
                parts.Visibility.HideOverlays = row.Entry == MenuEntry.HideOverlays;
                break;
            case MenuEntry.UndoGesture when parts.Gestures.Take() is { } back:
                Settings.Edit(back);
                break;
            case MenuEntry.ResetMinMax:
                parts.History.Reset([Settings.Config.Overlays[target]]);
                break;
            case MenuEntry.Exit:
                exit();
                break;
            case MenuEntry.ShowHiddenOverlays or MenuEntry.ShowOverlay or MenuEntry.HideThisOverlay or MenuEntry.ClickThrough or MenuEntry.Lock
                or MenuEntry.Graph or MenuEntry.Vertical or MenuEntry.Look or MenuEntry.Size or MenuEntry.Bigger or MenuEntry.Smaller:
                Write(row, MenuEdits.EditOf(row, [target], target, Looks()));
                break;

            // Copying the picture, the sensors' start and updates belong to the desktop's own tools on Linux.
            case MenuEntry.UndoGesture or MenuEntry.StartSensors or MenuEntry.CopyImage or MenuEntry.Separate or MenuEntry.GraphOwn
                or MenuEntry.SeparateFull or MenuEntry.Overlay or MenuEntry.Update or MenuEntry.SizeOnTaskbar:
            default:
                break;
        }

        parts.RequestPaint();
    }

    /// <summary>Opens the settings window, a process of its own, on an overlay and an item of it.</summary>
    public void OpenSettings(int overlay, string? item)
    {
        List<string> args = ["settings", ThermiCommandLine.OverlayOption, $"{overlay + 1}"];
        if (item is not null)
        {
            args.AddRange([ThermiCommandLine.ItemOption, item]);
        }

        Start(Environment.ProcessPath ?? "thermi", args);
    }

    // Writes only the difference, so what another process wrote meanwhile stays.
    private void Write(MenuRow row, Func<ThermiConfig, ThermiConfig>? edit)
    {
        var before = Settings.Config;
        var after = edit is null ? before : parts.Settle(before, edit(before)).Config;
        if (ConfigEdits.Replay(before, after) is not { } change)
        {
            return;
        }

        Settings.Edit(change);
        if (Menus.UndoOf(row) is { } undo)
        {
            parts.Gestures.Add(GestureKind.Menu, before, after, undo);
        }
    }

    private static bool Installed(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, program)));

    private void Start(string program, IEnumerable<string> args)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo(program, args) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            parts.Log.StartFailed(program, ex);
        }
    }
}
