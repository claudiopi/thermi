using System.Diagnostics;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Cli.Commands;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Core.Tray;
using Thermi.Linux.Gnome;
using Thermi.Linux.Logging;

namespace Thermi.Linux.Overlay;

/// <summary>A menu as shown: Core's rows, submenus' rows after their own, the Shell's rows alike, and the overlay it is of, if any.</summary>
internal sealed record ShownMenu(IReadOnlyList<MenuRow> All, IReadOnlyList<ShellRow> Rows, int? Overlay);

/// <summary>
/// An overlay's menu as the Shell's own popup menu: Core's rows, each with the text every OS's menu gives it, flattened
/// with the row each sits under, and the row chosen done by <see cref="MenuChoices"/>, as on every OS. The overlay reaches
/// no clipboard, so its menu offers no copy of it.
/// </summary>
internal sealed class OverlayMenu(OverlayParts parts, Action exit, IReadOnlyList<DesktopOverlay> overlays, Func<ShellScreens> screens) : IMenuHost
{
    // The system monitors GNOME, KDE and the newer GNOME apps ship, the first found opening.
    private static readonly string[] TaskManagers = ["gnome-system-monitor", "plasma-systemmonitor", "resources"];

    private ShownMenu _shown = new([], [], null);

    public Settings Settings => parts.Settings;

    public GestureUndo? Gestures => parts.Gestures;

    public ItemHistory History => parts.History;

    public int Overlays => overlays.Count;

    /// <summary>Shows overlay <paramref name="overlay"/>'s menu at a point of the stage, on <paramref name="item"/> or its edge.</summary>
    public void Show(int overlay, double x, double y, string? item, ServiceState service, Snapshot? known)
    {
        _shown = Rows(overlay, item, fromTray: false, service, known);
        _ = parts.Call(parts.Link.Menu((uint)overlay, x, y, _shown.Rows));
    }

    /// <summary>
    /// The rows of thermi's menu, with no <paramref name="overlay"/>, or of that overlay's, on <paramref name="item"/> or
    /// its edge, as the Shell's rows; an overlay that does not show has thermi's.
    /// </summary>
    public ShownMenu Rows(int? overlay, string? item, bool fromTray, ServiceState service, Snapshot? known)
    {
        var config = Settings.Config;
        var shown = overlay < Math.Min(config.Overlays.Count, overlays.Count) ? overlay : null;
        var undo = parts.Gestures.Offered(config);
        var looks = MenuChoices.Looks(Settings);
        var state = new MenuState(parts.Visibility.HideOverlays, Settings.Problem is not null, service, undo is null ? null : TimeSpan.Zero, looks, false)
        {
            CanCopy = false,
        };
        var texts = Texts.For(config.Language, System.Globalization.CultureInfo.CurrentUICulture);
        var place = new MenuPlace(config, texts, parts.Culture, shown) { UndoText = undo is { } offered ? texts.Of(offered) : null, Known = known };
        var all = new List<MenuRow>();
        var rows = new List<ShellRow>();
        Flatten(shown is { } at ? Menus.OfOverlay(config, at, state, item, fromTray) : Menus.Thermis(config, state), -1, place, all, rows);
        return new ShownMenu(all, rows, shown);
    }

    /// <summary>The row at <paramref name="index"/> of the overlay's menu last shown was chosen.</summary>
    public void Chosen(int index) => Choose(_shown, index);

    /// <summary>The row at <paramref name="index"/> of <paramref name="menu"/> was chosen.</summary>
    public void Choose(ShownMenu menu, int index)
    {
        if (index >= 0 && index < menu.All.Count)
        {
            MenuChoices.Choose(this, menu.All[index], menu.Overlay);
        }
    }

    public EditResult Settle(ThermiConfig before, ThermiConfig after) => parts.Settle(before, after);

    /// <summary>Opens the settings window, a process of its own, on an overlay and an item of it.</summary>
    public void OpenSettings(int overlay, string? item) => Open(overlay, item is null ? [] : [ThermiCommandLine.ItemOption, item]);

    public void OpenSize(int overlay) => Open(overlay, [ThermiCommandLine.SizeOption]);

    // The sensors' service is the system's, which its package starts; copying the picture is never offered.
    public void Reach(MenuEntry entry, int overlay)
    {
        if (entry == MenuEntry.TaskManager)
        {
            Start(TaskManagers.FirstOrDefault(Installed) ?? TaskManagers[0], [])?.Dispose();
        }
    }

    public void UndoGesture(int overlay)
    {
        if (parts.Gestures.Take() is { } back)
        {
            Settings.Edit(back);
        }
    }

    public void Separate(int overlay, string item) => overlays[overlay].Separate(item, screens());

    public void HideOverlays(bool hidden) => parts.Visibility.HideOverlays = hidden;

    public void Exit() => exit();

    public void RequestPaint() => parts.RequestPaint();

    private void Open(int overlay, string[] on)
    {
        parts.WindowOpening();
        List<string> args = ["settings", ThermiCommandLine.OverlayOption, $"{overlay + 1}", .. on];

        // The window writes its events on the standard error it inherits, thermi's, so one reader hears both.
        if (parts.Traced)
        {
            args.Add(ThermiCommandLine.TraceOption);
        }

        var program = Environment.ProcessPath ?? "thermi";
        if (Start(program, args) is { } window)
        {
            _ = EndedAsync(window);
        }
        else
        {
            parts.WindowFailed();
        }
    }

    // A window that dies, as it opens or later, would otherwise leave the person a click that did nothing. A second
    // request ends at once with 0, once the first window took it.
    private async Task EndedAsync(Process window)
    {
        using (window)
        {
            await window.WaitForExitAsync().ConfigureAwait(false);
            if (window.ExitCode != 0)
            {
                parts.Log.WindowEnded(window.ExitCode);
                parts.Post(parts.WindowFailed);
            }
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

    private static bool Installed(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, program)));

    private Process? Start(string program, IEnumerable<string> args)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(program, args) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            parts.Log.StartFailed(program, ex);
            return null;
        }
    }
}
