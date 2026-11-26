using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Thermi.App.Finder;
using Thermi.App.SettingsFile;
using Thermi.App.Window;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Search;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Windows.Install;
using Thermi.Windows.Logging;
using Thermi.Windows.Overlay;
using Thermi.Windows.Ui.Views;
using Thermi.Windows.Updates;
using Thermi.Windows.Win32;
using Thermi.Windows.Win32.Clipboard;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Thermi.Windows.Ui.Web;

/// <summary>
/// The C# side of the bridge, on the window's thread: the settings through the window's editor, the readings from the
/// pipe, the views, charts, catalog, finder and looks of <see cref="Views"/>, the texts in the window's language or the
/// long pseudo-language, and the desktop's picks. The driver and the service go through <see cref="WindowFixes"/>, and
/// the hotkeys through Windows and the overlay.
/// </summary>
internal sealed class WindowBridge : IWindowBridge, IDisposable
{
    // The version the build stamps, from Directory.Build.props.
    private static readonly string Version =
        typeof(WindowBridge).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private readonly WindowStart _start;
    private readonly ReadSensors _sensors;
    private readonly WindowEditor _editor;
    private readonly WindowViews _views;
    private readonly WindowCharts _charts;
    private readonly WindowPreviews _previews;
    private readonly FinderRun _finder;
    private readonly WindowLooks _looks;
    private readonly PageWatch _watch;
    private readonly WindowFixes _fixes;
    private readonly WindowUpdates _updates;
    private Texts _texts;
    private bool _lightTaskbar = Desktop.LightTaskbar();

    public WindowBridge(WindowStart start, WindowEditor editor, ChartHistory chart, Action<Action> onTurn)
    {
        _start = start;
        _editor = editor;
        _texts = start.Texts;
        _sensors = new ReadSensors(start.Environment.Sensors, start.Environment.Log);
        var readings = new Readings(start.Time);
        _views = new WindowViews(editor, _sensors, readings, chart.Items);
        _charts = new WindowCharts(editor, chart, _sensors, readings);
        _previews = new WindowPreviews(editor, _sensors, _charts, start.Environment.Screens);
        _finder = new FinderRun(start.Load, start.Time, onTurn, Tell, start.Environment.Log, () => _sensors.State);
        _looks = new WindowLooks(editor, new SavedStyles(ConfigStore.StylesFolder(start.Environment.SettingsPath)), Tell);
        _watch = new PageWatch(start.Environment with { Sensors = _sensors }, start.Time, onTurn, Tell, Arrived);
        _fixes = new WindowFixes(start.Environment, start.Time);
        _updates = new WindowUpdates(start.Time, start.Environment.Log);
        editor.Settle = (before, after) => _views.Settled(before, after, _texts, start.Environment.Screens());
        editor.Changed += (_, _) => Edited();
    }

    public event EventHandler<WindowChangeEventArgs>? Changed;

    public event EventHandler<WindowReadingsEventArgs>? Readings;

    public event EventHandler<DesktopPickEventArgs>? Picked;

    public OverlayLink? Link { get; set; }

    /// <summary>The window: the dialogs show over it, and "Exit thermi" closes it.</summary>
    public HWND Window
    {
        get => _looks.Owner;
        set => _looks.Owner = value;
    }

    /// <summary>
    /// A fresh snapshot, on the window's thread: kept for the charts and the catalog, given to the finder, and the page
    /// told of new readings, or of a change when other sensors came, which may name items or crowd an overlay anew, or the
    /// service's state changed. One the window cannot read changes nothing, and the page keeps what it shows.
    /// </summary>
    public void Arrived()
    {
        if (_sensors.Take(Read) is not { } read)
        {
            return;
        }

        if (_watch.Arrived(_sensors.Latest))
        {
            Changed?.Invoke(this, new WindowChangeEventArgs(sensors: true));
        }
        else
        {
            Readings?.Invoke(this, read);
        }
    }

    /// <summary>The monitors, the taskbars or a setting of Windows changed: the page reads all again.</summary>
    public void DesktopChanged()
    {
        _lightTaskbar = Desktop.LightTaskbar();
        Tell();
    }

    public void Pick(DesktopPickEventArgs pick) => Picked?.Invoke(this, pick);

    public Task<DesktopPickEventArgs?> Start() =>
        Task.FromResult(_start.Overlay > 0 ? new DesktopPickEventArgs(_start.Overlay - 1, _start.Item, SelectHow.DoubleClick) : null);

    public void Tell() => Changed?.Invoke(this, new WindowChangeEventArgs(sensors: false));

    public Task<ThermiConfig> Settings() => Ready(() => PageSettings.Of(_editor.Config, _texts, _sensors.Latest));

    public Task<ConfigProblemKind?> Problem() => Task.FromResult(_editor.Problem?.Kind);

    public Task<string?> Edit(PageEdit edit)
    {
        if (PageEdits.Elsewhere(edit))
        {
            return Task.FromResult(edit is LookEdit look ? _looks.Wear(look) : _charts.Line(edit, _views.Format(_texts)));
        }

        var snapshot = _sensors.Latest ?? new Snapshot([]);
        var result = _editor.Edit(edit.GetType().Name, PageEdits.Of(edit, snapshot, _texts, _start.Environment.AltGrKeys));
        return Task.FromResult(result.RefusalKey);
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

    public Task<WindowView> View() => Ready(() => _views.View(_texts));

    public Task<OverlayPicture?> Preview(int overlay, int room) => Ready(() => _previews.Preview(overlay, room, _texts));

    public Task<LookSamples> LookSamples(double scale) => Ready(() => _previews.Samples(_looks.Looks(), _texts, scale));

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

    public Task<Undone?> Redo() => Task.FromResult(TakeBack(_editor.RedoName, _editor.Redo, redo: true));

    public Task<WindowStatus> Status() => Ready(StatusNow);

    public Task<UndoSteps> Steps() => Task.FromResult(WindowFacts.Steps(_editor.UndoName, _editor.RedoName, _views.Format(_texts)));

    public Task<About> About() => Task.FromResult(
        WindowFacts.About(Version, CultureInfo.CurrentUICulture, _updates.Line(), _views.Format(_texts), _start.Time.LocalTimeZone));

    public Task CheckUpdates()
    {
        _updates.Ask();
        return Task.CompletedTask;
    }

    public Task RestartToUpdate()
    {
        if (_updates.Restart())
        {
            PInvoke.PostMessage(Window, PInvoke.WM_CLOSE, 0, 0);
        }

        return Task.CompletedTask;
    }

    public Task Open(Opened what)
    {
        WindowLinks.Open(what, _start.Environment.SettingsPath, _start.Environment.Log);
        return Task.CompletedTask;
    }

    public Task<AgentsView> Agents() => Task.FromResult(WindowAgents.View(_views.Format(_texts), _start.Time.LocalTimeZone));

    public Task Copy(string text)
    {
        ClipboardData.WriteText(Window, text);
        return Task.CompletedTask;
    }

    // As an import does, once the page's message has been answered.
    public async Task<string?> PasteLook(int overlay)
    {
        await Task.Yield();
        return ClipboardText.Read(Window) is { } text
            ? _looks.Import(overlay, text, _texts)
            : Core.Translations.Texts.Key(Refusal.PasteFailed);
    }

    public Task<SensorView> Snapshot() =>
        Ready(() => SensorView.Of(
            _sensors.State, _sensors.Latest, _editor.Config.SensorNames, _texts, _sensors.Taken));

    public Task<IReadOnlyList<double?>> History(string id) => Task.FromResult(_charts.History(id));

    public Task<IReadOnlyDictionary<string, string>> Texts() => Task.FromResult(_texts.All);

    public Task<IReadOnlyList<Rect?>> Places() => Task.FromResult(_views.Places(_texts, _start.Environment.Screens()));

    public Task<IReadOnlyList<MonitorView>> Monitors() =>
        Task.FromResult<IReadOnlyList<MonitorView>>([.. _start.Environment.Screens().Select(MonitorView.Of)]);

    public Task Report(string error)
    {
        _start.Environment.Log.PageThrew(error);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, StyleConfig>> Looks() => Task.FromResult(_looks.Looks());

    public Task<IReadOnlyList<WornLook?>> Worn() => Task.FromResult(_looks.Worn());

    public Task<string?> SaveLook(int overlay, string name) => Task.FromResult(_looks.Save(overlay, name));

    public Task<string?> RenameLook(string name, string to) => Task.FromResult(_looks.Rename(name, to));

    public Task<string?> DeleteLook(string name) => Task.FromResult(_looks.Delete(name));

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

    public Task<CatalogView> Catalog(string search, IReadOnlyList<SensorKind> kinds, bool all) =>
        Task.FromResult(_views.Catalog(search, kinds, all, _texts));

    public Task<IReadOnlyList<Found>> Search(string query, IReadOnlyList<Findable> among) =>
        Task.FromResult(new SearchIndex(among).Find(query));

    public Task FindCpuFan()
    {
        _finder.Start(_sensors.Latest);
        return Task.CompletedTask;
    }

    public Task CancelFind()
    {
        _finder.Cancel();
        return Task.CompletedTask;
    }

    public Task<FinderView> Finder() =>
        Task.FromResult(_finder.View(_sensors.Latest, _texts, CultureInfo.CurrentCulture, _editor.Config.SensorNames));

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

    public Task<DriverInstall> InstallDriver() => _fixes.InstallDriver();

    public Task<bool> StartService() => _fixes.StartService();

    public Task<bool> HotkeyHeld(string combination) => Task.FromResult(HotkeyRegistry.HeldElsewhere(combination, _editor.Config.Hotkeys));

    public Task RestHotkeys(bool rest)
    {
        _start.Environment.RestHotkeys(rest);
        _watch.AskHotkeys();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _finder.Dispose();
        _watch.Dispose();
    }

    // What reads the readings waits for the first ones, as PageWatch says.
    private Task<T> Ready<T>(Func<T> read) => _watch.AfterFirst(read);

    // All a snapshot changes, which Core may fail to read.
    private WindowReadingsEventArgs Read()
    {
        _views.Arrived();
        _charts.Arrived();
        if (_sensors.Latest is { } snapshot)
        {
            _finder.Add(snapshot);
        }

        return new WindowReadingsEventArgs(_views.View(_texts), StatusNow());
    }

    private WindowStatus StatusNow()
    {
        var status = WindowFacts.Status(
            _editor.Config,
            _sensors.State,
            _sensors.Latest,
            _looks.AnySaved,
            _watch.Hotkeys,
            _editor.Problem,
            _views.Format(_texts));
        return status with { LightTaskbar = _lightTaskbar, ForEveryone = SensorPipes.InstallsForEveryone };
    }

    // An undo or a redo, named before it runs, and what it changed back once it did; null when it changed nothing.
    private Undone? TakeBack(StepName? name, Func<UndoResult?> take, bool redo = false)
    {
        var before = _editor.Config;
        return name is not null && take() is { Applied: > 0 }
            ? WindowFacts.UndoneOf(name, before, _editor.Config, _views.Format(_texts), redo)
            : null;
    }

    // A language chosen or undone takes every text at once; the pseudo-language stays as it was asked for.
    private void Edited()
    {
        if (!_start.Pseudo)
        {
            _texts = Core.Translations.Texts.For(_editor.Config.Language, CultureInfo.CurrentUICulture);
        }

        _watch.Edited();
        DesktopChanged();
    }
}
