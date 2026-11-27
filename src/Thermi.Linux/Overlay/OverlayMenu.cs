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

/// <summary>
/// An overlay's menu as the Shell's own popup menu: Core's rows, each with the text every OS's menu gives it, flattened
/// with the row each sits under, and the row chosen done by <see cref="MenuChoices"/>, as on every OS. The overlay reaches
/// no clipboard, so its menu offers no copy of it.
/// </summary>
internal sealed class OverlayMenu(OverlayParts parts, Action exit, IReadOnlyList<ShellOverlay> overlays, Func<ShellScreens> screens) : IMenuHost
{
    // The system monitors GNOME, KDE and the newer GNOME apps ship, the first found opening.
    private static readonly string[] TaskManagers = ["gnome-system-monitor", "plasma-systemmonitor", "resources"];

    // Where a program reads the token it was started with, as a launcher sets them: Wayland's and X11's. GIO, GTK and Qt
    // read both.
    private static readonly string[] Tokens = ["XDG_ACTIVATION_TOKEN", "DESKTOP_STARTUP_ID"];

    // The Shell's time for now: a menu's choice tells none.
    private const uint Now = 0;

    private IReadOnlyList<MenuRow> _shown = [];
    private int _overlay;

    public Settings Settings => parts.Settings;

    public GestureUndo? Gestures => parts.Gestures;

    public ItemHistory History => parts.History;

    public int Overlays => overlays.Count;

    /// <summary>Shows overlay <paramref name="overlay"/>'s menu at a point of the stage, on <paramref name="item"/> or its edge.</summary>
    public void Show(int overlay, double x, double y, string? item, ServiceState service, Snapshot? known)
    {
        var config = Settings.Config;
        var undo = parts.Gestures.Offered(config);
        var looks = MenuChoices.Looks(Settings);
        var state = new MenuState(parts.Visibility.HideOverlays, Settings.Problem is not null, service, undo is null ? null : TimeSpan.Zero, looks, false)
        {
            CanCopy = false,
        };
        var texts = Texts.For(config.Language, System.Globalization.CultureInfo.CurrentUICulture);
        var place = new MenuPlace(config, texts, parts.Culture, overlay) { UndoText = undo is { } offered ? texts.Of(offered) : null, Known = known };
        var all = new List<MenuRow>();
        var rows = new List<ShellRow>();
        Flatten(Menus.OfOverlay(config, overlay, state, item), -1, place, all, rows);
        (_shown, _overlay) = (all, overlay);
        _ = parts.Call(parts.Link.Menu((uint)overlay, x, y, rows));
    }

    /// <summary>The row at <paramref name="index"/> of the menu last shown was chosen.</summary>
    public void Chosen(int index)
    {
        if (index >= 0 && index < _shown.Count)
        {
            MenuChoices.Choose(this, _shown[index], _overlay);
        }
    }

    public EditResult Settle(ThermiConfig before, ThermiConfig after) => parts.Settle(before, after);

    /// <summary>Opens the settings window, a process of its own, on an overlay and an item of it.</summary>
    public void OpenSettings(int overlay, string? item) => OpenSettings(overlay, item, Now);

    /// <summary>Opens the settings window as the menu's choice does, on a click at a time of the Shell's.</summary>
    public void OpenSettings(int overlay, string? item, uint time) => Open(overlay, item is null ? [] : [ThermiCommandLine.ItemOption, item], time);

    public void OpenSize(int overlay) => Open(overlay, [ThermiCommandLine.SizeOption], Now);

    // The sensors' service is the system's, which its package starts; copying the picture is never offered.
    public void Reach(MenuEntry entry, int overlay)
    {
        if (entry == MenuEntry.TaskManager)
        {
            Start(TaskManagers.FirstOrDefault(Installed) ?? TaskManagers[0], [], Now);
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

    private void Open(int overlay, string[] on, uint time)
    {
        List<string> args = ["settings", ThermiCommandLine.OverlayOption, $"{overlay + 1}", .. on];

        // The window writes its events on the standard error it inherits, thermi's, so one reader hears both.
        if (parts.Traced)
        {
            args.Add(ThermiCommandLine.TraceOption);
        }

        Start(Environment.ProcessPath ?? "thermi", args, time);
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

    private void Start(string program, IEnumerable<string> args, uint time) => _ = StartAsync(program, args, time);

    // The program comes in front with the desktop's token for the click, and never with the one thermi was started with,
    // long spent.
    private async Task StartAsync(string program, IEnumerable<string> args, uint time)
    {
        var start = new ProcessStartInfo(program, args) { UseShellExecute = false };
        var token = "";
        try
        {
            token = await parts.Activation(time).ConfigureAwait(false);
        }
        catch (GLib.GException ex)
        {
            parts.Log.ShellCallFailed(ex);
        }

        foreach (var name in Tokens)
        {
            if (token.Length > 0)
            {
                start.Environment[name] = token;
            }
            else
            {
                start.Environment.Remove(name);
            }
        }

        try
        {
            using var started = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            parts.Log.StartFailed(program, ex);
        }
    }
}
