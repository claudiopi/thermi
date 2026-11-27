using System.Globalization;
using System.Reflection;
using Thermi.App.Finder;
using Thermi.App.Logging;
using Thermi.App.SettingsFile;
using Thermi.Core.Agents;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Messages;
using Thermi.Core.Search;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;

namespace Thermi.App.Window;

/// <summary>
/// The C# side of the bridge, alike on every OS, on the window's thread: the settings through the window's editor, the
/// readings from the sensors, the views, charts, catalog, finder and looks, the texts in the window's language or the long
/// pseudo-language, and the desktop's picks. What only an OS can do, its painter, clipboard, dialogs, hotkeys, fixes and
/// updates, it asks of its <see cref="IWindowHost"/>.
/// </summary>
internal class PageBridge : IWindowBridge, IDisposable
{
    /// <summary>thermi's repository, where About's pages and its downloads are.</summary>
    public const string Repository = "https://github.com/claudiopi/thermi";

    private static readonly string Version =
        typeof(PageBridge).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private readonly IWindowHost _host;
    private readonly PageStart _start;
    private readonly ReadSensors _sensors;
    private readonly WindowEditor _editor;
    private readonly WindowViews _views;
    private readonly WindowCharts _charts;
    private readonly WindowPreviews _previews;
    private readonly FinderRun _finder;
    private readonly WindowLooks _looks;
    private readonly PageWatch _watch;
    private Texts _texts;
    private bool _lightTaskbar;

    public PageBridge(IWindowHost host, PageStart start, WindowEditor editor, ChartHistory chart, Action<Action> onTurn)
    {
        _host = host;
        _start = start;
        _editor = editor;
        _texts = host.Desktop.Words(start.Texts);
        _lightTaskbar = host.LightTaskbar();
        _sensors = new ReadSensors(start.Sensors, start.Log);
        var readings = new Readings(start.Time);
        _views = new WindowViews(editor, _sensors, readings, chart.Items, host);
        _charts = new WindowCharts(editor, chart, _sensors, readings, host);
        _previews = new WindowPreviews(editor, _sensors, _charts, host);
        _finder = new FinderRun(start.Load, start.Time, onTurn, Tell, start.Log, () => _sensors.State);
        _looks = new WindowLooks(editor, new SavedStyles(ConfigStore.StylesFolder(start.SettingsPath)), host, Tell);
        _watch = new PageWatch(_sensors, host, start.Time, onTurn, Tell, Arrived);
        editor.Settle = (before, after) => _views.Settled(before, after, _texts, host.Screens());
        editor.Changed += (_, _) => Edited();
    }

    public event EventHandler<WindowChangeEventArgs>? Changed;

    public event EventHandler<WindowReadingsEventArgs>? Readings;

    public event EventHandler<DesktopPickEventArgs>? Picked;

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

    /// <summary>The monitors, the taskbars or a setting of the desktop changed: the page reads all again.</summary>
    public void DesktopChanged()
    {
        _lightTaskbar = _host.LightTaskbar();
        Tell();
    }

    public void Pick(DesktopPickEventArgs pick) => Picked?.Invoke(this, pick);

    public Task<DesktopPickEventArgs?> Start() =>
        Task.FromResult(_start.Overlay > 0
            ? new DesktopPickEventArgs(_start.Overlay - 1, _start.Item, _start.SizeRow ? SelectHow.Size : SelectHow.DoubleClick)
            : null);

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
        var (made, said) = edit is TypedEdit typed ? TypedNumbers.Read(_editor.Config, typed, _views.Format(_texts))
            : new TypedAnswer(PageEdits.Of(edit, snapshot, _texts, _host.AltGrKeys), null);
        var result = made is null ? null : _editor.Edit(edit.GetType().Name, made);
        return Task.FromResult(result?.RefusalKey ?? said);
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

    public Task<WindowView> View() => Ready(() => _views.View(_texts, _host.Screens));

    public Task<OverlayPicture?> Preview(int overlay, int room) => Ready(() => _previews.Preview(overlay, room, _texts));

    public Task<LookSamples> LookSamples(double scale) => Ready(() => _previews.Samples(_looks.Looks(), _texts, scale));

    public Task<ChartView?> Chart(string id) => Task.FromResult(_charts.Chart(id, _views.Format(_texts)));

    public Task ExitThermi()
    {
        _host.ExitThermi();
        return Task.CompletedTask;
    }

    public Task<Undone?> Undo() => Task.FromResult(TakeBack(_editor.UndoName, _editor.Undo));

    public Task<Undone?> Redo() => Task.FromResult(TakeBack(_editor.RedoName, _editor.Redo, redo: true));

    public Task<WindowStatus> Status() => Ready(StatusNow);

    public Task<UndoSteps> Steps() => Task.FromResult(WindowFacts.Steps(_editor.UndoName, _editor.RedoName, _views.Format(_texts)));

    public Task<About> About() => Task.FromResult(
        WindowFacts.About(Version, CultureInfo.CurrentUICulture, _host.Updates(), _views.Format(_texts), _start.Time.LocalTimeZone));

    public Task CheckUpdates()
    {
        _host.CheckUpdates();
        return Task.CompletedTask;
    }

    public Task RestartToUpdate()
    {
        _host.RestartToUpdate();
        return Task.CompletedTask;
    }

    public Task Open(Opened what)
    {
        _host.Open(LinkOf(what, _start.SettingsPath), _start.Log);
        return Task.CompletedTask;
    }

    public Task<AgentsView> Agents() => Task.FromResult(_host.Agents(_views.Format(_texts), _start.Time.LocalTimeZone));

    public Task<bool> Copy(string text) => Task.FromResult(_host.Copy(text));

    // As an import does, once the page's message has been answered.
    public async Task<string?> PasteLook(int overlay)
    {
        await Task.Yield();
        return await _host.Paste() is { } text
            ? _looks.Import(overlay, text)
            : Core.Translations.Texts.Key(Refusal.PasteFailed);
    }

    public Task<SensorView> Snapshot() =>
        Ready(() => SensorView.Of(
            _sensors.State, _sensors.Latest, _editor.Config.SensorNames, _texts, _sensors.Taken));

    public Task<IReadOnlyList<double?>> History(string id) => Task.FromResult(_charts.History(id));

    public Task<IReadOnlyDictionary<string, string>> Texts() => Task.FromResult(_texts.All);

    public Task<IReadOnlyList<Rect?>> Places() => Task.FromResult(_views.Places(_texts, _host.Screens()));

    public Task<IReadOnlyList<MonitorView>> Monitors() =>
        Task.FromResult<IReadOnlyList<MonitorView>>([.. _host.Screens().Select(MonitorView.Of)]);

    public Task Report(string error)
    {
        _start.Log.PageThrew(error);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, StyleConfig>> Looks() => Task.FromResult(_looks.Looks());

    public Task<IReadOnlyList<WornLook?>> Worn() => Task.FromResult(_looks.Worn());

    public Task<string?> SaveLook(int overlay, string name) => Task.FromResult(_looks.Save(overlay, name));

    public Task<string?> RenameLook(string name, string to) => Task.FromResult(_looks.Rename(name, to));

    public Task<string?> DeleteLook(string name) => Task.FromResult(_looks.Delete(name));

    // A dialog runs once the page's message has been answered, never inside the web view's own event.
    public async Task ExportLook(int overlay)
    {
        await Task.Yield();
        await _looks.Export(overlay, _texts);
    }

    public async Task ExportSavedLook(string name)
    {
        await Task.Yield();
        await _looks.ExportSaved(name, _texts);
    }

    public async Task<string?> ImportLook(int overlay, string? text)
    {
        await Task.Yield();
        return text is null ? await _looks.ImportFile(overlay, _texts) : _looks.Import(overlay, text);
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

    public Task<FinderView> Finder()
    {
        var view = _finder.View(_sensors.Latest, _texts, CultureInfo.CurrentCulture, _editor.Config.SensorNames);
        return Task.FromResult(view with { Refusal = _host.Desktop.FinderRefusal(view.Refusal) });
    }

    public Task Caption(CaptionAction action)
    {
        _host.Caption(action);
        return Task.CompletedTask;
    }

    public Task StartOverlay()
    {
        _host.StartOverlay();
        return Task.CompletedTask;
    }

    public Task Restore()
    {
        _editor.Restore();
        return Task.CompletedTask;
    }

    public Task<bool> StartWithWindows() => Task.FromResult(_host.StartsAtSignIn);

    public Task SetStartWithWindows(bool on)
    {
        _host.StartsAtSignIn = on;
        Tell();
        return Task.CompletedTask;
    }

    public Task Editing(int? overlay, string? selected, string? pointed)
    {
        _host.Editing(overlay, selected, pointed);
        return Task.CompletedTask;
    }

    public Task<DriverInstall> InstallDriver() => _host.Desktop.OffersDriver ? _host.InstallDriver() : Task.FromResult(DriverInstall.NotInstalled);

    public Task<bool> StartService() => _host.StartService();

    public async Task<bool> InstallExtension()
    {
        var installed = _host.Desktop.ExtensionFix is not null && await _host.InstallExtension();
        Tell();
        return installed;
    }

    public Task<bool> HotkeyHeld(string combination) => Task.FromResult(_host.HotkeyHeld(combination, _editor.Config.Hotkeys));

    public Task RestHotkeys(bool rest)
    {
        _host.RestHotkeys(rest);
        _watch.AskHotkeys();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _finder.Dispose();
            _watch.Dispose();
            _views.Dispose();
        }
    }

    private static string LinkOf(Opened what, string settingsPath) => what switch
    {
        Opened.SettingsFile => settingsPath,
        Opened.AgentsLog => AgentsLog.Newest(ThermiLog.AgentsPath),
        Opened.Source => Repository,
        Opened.Report => $"{Repository}/issues/new/choose",
        Opened.Notices or _ => $"{Repository}/blob/main/THIRD-PARTY-NOTICES.md",
    };

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

        return new WindowReadingsEventArgs(_views.View(_texts, _host.Screens), StatusNow());
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
        return _host.Desktop.Of(status with { LightTaskbar = _lightTaskbar, ForEveryone = _host.ForEveryone });
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
            _texts = _host.Desktop.Words(Core.Translations.Texts.For(_editor.Config.Language, CultureInfo.CurrentUICulture));
        }

        _watch.Edited();
        DesktopChanged();
    }
}
