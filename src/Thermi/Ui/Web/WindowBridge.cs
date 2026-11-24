using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Thermi.Commands;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Placement;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Logging;
using Thermi.Overlay;
using Thermi.Painting;
using Thermi.Ui.Editor;
using Thermi.Ui.Views;
using Thermi.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Ui.Web;

/// <summary>
/// The C# side of the bridge, on the window's thread: the settings through the window's editor, the readings from the
/// pipe, the views, charts, catalog, finder and looks of <see cref="Views"/>, the texts in the window's language or the
/// long pseudo-language, and the desktop's picks. The driver and the service go through the elevated mode, behind one
/// UAC prompt each, and the hotkeys through Windows and the overlay.
/// </summary>
internal sealed class WindowBridge : IWindowBridge, IDisposable
{
    // The sensor service restarts after the driver's install, and its pipe comes back within a few reads.
    private static readonly TimeSpan DriverReadsWithin = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DriverPoll = TimeSpan.FromMilliseconds(500);

    private const string Repository = "https://github.com/claudiopi/thermi";

    // The version the build stamps, without the commit it adds after a plus.
    private static readonly string Version = (typeof(WindowBridge).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+')[0];

    private readonly WindowStart _start;
    private readonly WindowEditor _editor;
    private readonly WindowViews _views;
    private readonly WindowCharts _charts;
    private readonly WindowFinder _finder;
    private readonly WindowLooks _looks;
    private readonly Painter _painter = new();
    private readonly TaskbarApps _apps;
    private Texts _texts;
    private IReadOnlyList<int> _crowded = [];

    public WindowBridge(WindowStart start, WindowEditor editor, ChartHistory chart, Action<Action> onTurn)
    {
        _start = start;
        _editor = editor;
        _texts = start.Texts;
        var readings = new Readings(start.Time);
        _views = new WindowViews(editor, start.Environment.Sensors, readings);
        _charts = new WindowCharts(editor, chart, start.Environment.Sensors, readings, start.Time);
        _finder = new WindowFinder(start.Load, start.Time, onTurn, Tell);
        _looks = new WindowLooks(editor, new SavedStyles(ConfigStore.StylesFolder(start.Environment.SettingsPath)), Tell);
        _apps = new TaskbarApps(start.Time, onTurn, AppsMoved);
        editor.Changed += (_, _) => Edited();
    }

    public event EventHandler? Changed;

    public event EventHandler<DesktopPickEventArgs>? Picked;

    public OverlayLink? Link { get; set; }

    /// <summary>The window: the dialogs show over it, and "Exit thermi" closes it.</summary>
    public HWND Window
    {
        get => _looks.Owner;
        set => _looks.Owner = value;
    }

    /// <summary>A fresh snapshot, on the window's thread: kept for the charts and the catalog, given to the finder, and the page told.</summary>
    public void Arrived()
    {
        _views.Arrived();
        _charts.Arrived();
        if (_start.Environment.Sensors.Latest is { } snapshot)
        {
            _finder.Add(snapshot);
        }

        Recrowd();
        Tell();
    }

    public void Pick(DesktopPickEventArgs pick) => Picked?.Invoke(this, pick);

    public Task<DesktopPickEventArgs?> Start() =>
        Task.FromResult(_start.Overlay > 0 ? new DesktopPickEventArgs(_start.Overlay - 1, _start.Item, SelectHow.DoubleClick) : null);

    public void Tell() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<ThermiConfig> Settings() => Task.FromResult(PageSettings.Of(_editor.Config, _texts, _start.Environment.Sensors.Latest));

    public Task<ConfigProblemKind?> Problem() => Task.FromResult(_editor.Problem?.Kind);

    public Task<string?> Edit(PageEdit edit)
    {
        if (PageEdits.Elsewhere(edit))
        {
            return Task.FromResult(edit is LookEdit look ? _looks.Wear(look) : _charts.Line(edit, _views.Format(_texts)));
        }

        var snapshot = _start.Environment.Sensors.Latest ?? new Snapshot([]);
        var result = _editor.Edit(edit.GetType().Name, PageEdits.Of(edit, snapshot, _texts));
        return Task.FromResult(result.Refused is { } refused ? Core.Translations.Texts.Key(refused) : null);
    }

    public Task Gesture(bool active)
    {
        if (active)
        {
            _editor.BeginGesture();
        }
        else
        {
            _editor.EndGesture();
        }

        return Task.CompletedTask;
    }

    public Task<WindowView> View() => Task.FromResult(_views.View(_texts));

    public Task<ChartView?> Chart(string id) => Task.FromResult(_charts.Chart(id, _views.Format(_texts)));

    /// <summary>The overlay is asked to close, as its tray's "Exit thermi" closes it, and then the window closes.</summary>
    public Task ExitThermi()
    {
        var host = PInvoke.FindWindowEx(HWND.Null, HWND.Null, HiddenWindow.HostClass, null);
        if (!host.IsNull)
        {
            PInvoke.PostMessage(host, PInvoke.WM_CLOSE, 0, 0);
        }

        PInvoke.PostMessage(Window, PInvoke.WM_CLOSE, 0, 0);
        return Task.CompletedTask;
    }

    public Task<Undone?> Undo() => Task.FromResult(TakeBack(_editor.UndoName, _editor.Undo));

    public Task<Undone?> Redo() => Task.FromResult(TakeBack(_editor.RedoName, _editor.Redo));

    public Task<WindowStatus> Status()
    {
        var sensors = _start.Environment.Sensors;
        return Task.FromResult(WindowFacts.Status(_editor.Config, sensors.State, sensors.Latest, _looks.Looks().Count > 0, _start.Environment.HotkeyStatus(), _editor.Problem, _views.Format(_texts)));
    }

    public Task<UndoSteps> Steps() => Task.FromResult(WindowFacts.Steps(_editor.UndoName, _editor.RedoName, _views.Format(_texts)));

    public Task<About> About() => Task.FromResult(WindowFacts.About(Version, CultureInfo.CurrentUICulture));

    public Task Open(Opened what)
    {
        var target = what switch
        {
            Opened.SettingsFile => _start.Environment.SettingsPath,
            Opened.Source => Repository,
            Opened.Report => $"{Repository}/issues/new/choose",
            Opened.Notices or _ => $"{Repository}/blob/main/THIRD-PARTY-NOTICES.md",
        };
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            _start.Environment.Log.OpenFailed(target, ex);
        }

        return Task.CompletedTask;
    }

    // As an import does, once the page's message has been answered.
    public async Task<string?> PasteLook(int overlay)
    {
        await Task.Yield();
        return ClipboardText.Read(Window) is { } text ? _looks.Import(overlay, text, _texts) : Core.Translations.Texts.Key(Refusal.PasteFailed);
    }

    public Task<SensorView> Snapshot() => Task.FromResult(SensorView.Of(_start.Environment.Sensors.State, _start.Environment.Sensors.Latest, _editor.Config.SensorNames));

    public Task<IReadOnlyList<double?>> History(string id) => Task.FromResult(_charts.History(id));

    public Task<IReadOnlyDictionary<string, string>> Texts() => Task.FromResult(_texts.All);

    public Task<IReadOnlyList<MonitorView>> Monitors() =>
        Task.FromResult<IReadOnlyList<MonitorView>>([.. _start.Environment.Screens().Select(screen => new MonitorView(screen.Id, screen.Primary, RectOf(screen.Bounds), RectOf(screen.Work), screen.Scale, screen.Taskbar is not null, Snaps.Floats(screen)))]);

    public Task<IReadOnlyList<int>> Crowded() => Task.FromResult(_crowded);

    public Task Report(string error)
    {
        _start.Environment.Log.PageThrew(error);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, StyleConfig>> Looks() => Task.FromResult(_looks.Looks());

    public Task<string?> SaveLook(int overlay, string name) => Task.FromResult(_looks.Save(overlay, name));

    public Task<string?> RenameLook(string name, string to) => Task.FromResult(_looks.Rename(name, to));

    public Task DeleteLook(string name)
    {
        _looks.Delete(name);
        return Task.CompletedTask;
    }

    // A dialog runs once the page's message has been answered, never inside WebView2's own event.
    public async Task ExportLook(int overlay)
    {
        await Task.Yield();
        _looks.Export(overlay, _texts);
    }

    public async Task ExportSavedLook(string name)
    {
        await Task.Yield();
        _looks.ExportSaved(name, _texts);
    }

    public async Task<string?> ImportLook(int overlay, string? text)
    {
        await Task.Yield();
        return _looks.Import(overlay, text, _texts);
    }

    public Task<CatalogView> Catalog(string search, IReadOnlyList<SensorKind> kinds, bool all) => Task.FromResult(_views.Catalog(search, kinds, all, _texts));

    public Task FindCpuFan()
    {
        _finder.Start(_start.Environment.Sensors.Latest);
        return Task.CompletedTask;
    }

    public Task CancelFind()
    {
        _finder.Cancel();
        return Task.CompletedTask;
    }

    public Task<FinderView> Finder() => Task.FromResult(_finder.View(_start.Environment.Sensors.Latest, _texts, CultureInfo.CurrentCulture, _editor.Config.SensorNames));

    public Task StartOverlay()
    {
        try
        {
            Program.Start(_start.Environment.ExePath, _start.Environment.Log.IsEnabled(LogLevel.Debug));
        }
        catch (Win32Exception ex)
        {
            _start.Environment.Log.OverlayStartFailed(ex);
        }

        return Task.CompletedTask;
    }

    public Task Restore()
    {
        _editor.Restore();
        return Task.CompletedTask;
    }

    public Task<bool> StartWithWindows() => Task.FromResult(_start.Environment.Startup.On);

    public Task SetStartWithWindows(bool on)
    {
        _start.Environment.Startup.Set(on);
        Tell();
        return Task.CompletedTask;
    }

    public Task Editing(int? overlay, string? selected, string? pointed)
    {
        Link?.Editing(overlay, selected, pointed);
        return Task.CompletedTask;
    }

    /// <summary>True only once a CPU temperature, power or clock reads, after the sensor service opened the new driver.</summary>
    public async Task<bool> InstallDriver()
    {
        if (await ElevateAsync(Commands.InstallDriver.Verb) is not Commands.InstallDriver.Installed)
        {
            return false;
        }

        for (var start = _start.Time.GetTimestamp(); _start.Time.GetElapsedTime(start) < DriverReadsWithin; await Task.Delay(DriverPoll, _start.Time))
        {
            if (_start.Environment.Sensors.Latest is { } snapshot && !SensorMap.DriverMayBeMissing(snapshot))
            {
                return true;
            }
        }

        return false;
    }

    // The window recovers by seeing the pipe come back, whatever the answer.
    public async Task<bool> StartService() => await ElevateAsync(StartSensors.Verb) is StartSensors.Started;

    public Task<bool> HotkeyHeld(string combination) => Task.FromResult(HotkeyRegistry.HeldElsewhere(combination, _editor.Config.Hotkeys));

    public Task RestHotkeys(bool rest)
    {
        _start.Environment.RestHotkeys(rest);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _finder.Dispose();
        _painter.Dispose();
    }

    private static Rect RectOf(PixelRect rect) => new(rect.Left, rect.Top, rect.Width, rect.Height);

    // An undo or a redo, named before it runs, and what it changed back once it did; null when it changed nothing.
    private Undone? TakeBack(StepName? name, Func<UndoResult?> take)
    {
        var before = _editor.Config;
        return name is not null && take() is { Applied: > 0 } ? WindowFacts.UndoneOf(name, before, _editor.Config, _views.Format(_texts)) : null;
    }

    // Starting a process behind UAC blocks its caller until the prompt closes, so it runs off the window's thread. Null
    // when the prompt was refused or the mode could not start.
    private async Task<int?> ElevateAsync(string verb)
    {
        try
        {
            var code = await Task.Run(() => _start.Environment.Elevate(_start.Environment.ExePath, verb));
            if (code is { } ended and not 0)
            {
                _start.Environment.Log.ElevatedEnded(verb, ended);
            }

            return code;
        }
        catch (Win32Exception ex)
        {
            _start.Environment.Log.ElevatedFailed(verb, ex);
            return null;
        }
    }

    // A language chosen or undone takes every text at once; the pseudo-language stays as it was asked for.
    private void Edited()
    {
        if (!_start.Pseudo)
        {
            _texts = Core.Translations.Texts.For(_editor.Config.Language, CultureInfo.CurrentUICulture);
        }

        Recrowd();
        Tell();
    }

    // The app buttons' new extent tells the page only when it changes which overlays fit.
    private void AppsMoved()
    {
        if (Recrowd())
        {
            Tell();
        }
    }

    // Which overlays do not fit beside the app buttons, with their last read extent; true when that changed.
    private bool Recrowd()
    {
        var config = _editor.Config;
        var screens = _start.Environment.Screens();
        var snapshot = _start.Environment.Sensors.Latest;
        IReadOnlyList<int> crowded = [.. Enumerable.Range(0, config.Overlays.Count).Where(overlay => TaskbarRoom.Crowded(_painter, _apps, config, overlay, snapshot, screens))];
        var changed = !crowded.SequenceEqual(_crowded);
        _crowded = crowded;
        return changed;
    }
}
