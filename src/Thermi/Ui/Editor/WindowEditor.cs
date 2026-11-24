using Microsoft.Extensions.Logging;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Settings;
using Thermi.Logging;
using Thermi.Overlay;

namespace Thermi.Ui.Editor;

/// <summary>Why the last edit, undo or redo did not do all it was asked: refused by Core's rules, or part of its step skipped.</summary>
internal sealed record EditNote(Refusal? Refused, string? Problem, bool Skipped);

/// <summary>
/// The window's side of the settings: what it shows, its edits and their undo. Every edit goes through Core's gate and is
/// written through <see cref="Settings"/>, as a replay of what it changed over whatever the file holds then, so what
/// another process wrote meanwhile stays. A change from outside shows at once, except during a gesture, which it waits
/// for, so that the gesture's one undo step holds the window's own changes only. Used on the window's thread alone.
/// </summary>
internal sealed class WindowEditor
{
    private readonly Settings _settings;
    private readonly Action<Action> _onTurn;
    private readonly ILogger _log;
    private readonly UndoHistory _history;
    private bool _inGesture;
    private bool _held;

    /// <param name="settings">The settings file, already read.</param>
    /// <param name="onTurn">Runs an action on the window's thread, where the settings' changes, raised on any thread, are taken.</param>
    /// <param name="log">Where each input is logged with <c>--trace</c>.</param>
    /// <param name="time">The clock by which repeated changes of one field within a second are one step.</param>
    public WindowEditor(Settings settings, Action<Action> onTurn, ILogger log, TimeProvider time)
    {
        _settings = settings;
        _onTurn = onTurn;
        _log = log;
        _history = new UndoHistory(time);
        Config = settings.Config;
        settings.Changed += SettingsChanged;
    }

    public event EventHandler? Changed;

    /// <summary>Raised after every edit, undo and redo, with its <see cref="Note"/>.</summary>
    public event EventHandler? Noted;

    public ThermiConfig Config { get; private set; }

    /// <summary>Why the last edit, undo or redo did not do all it was asked, or null when it did.</summary>
    public EditNote? Note { get; private set; }

    /// <summary>Why the settings file cannot be used, or null when it can; while it cannot, nothing changes.</summary>
    public ConfigProblem? Problem => _settings.Problem;

    public bool CanUndo => Problem is null && _history.CanUndo;

    public bool CanRedo => Problem is null && _history.CanRedo;

    /// <summary>The step Undo would take back, or null when it would take none.</summary>
    public StepName? UndoName => CanUndo ? _history.UndoName : null;

    public StepName? RedoName => CanRedo ? _history.RedoName : null;

    /// <summary>Makes an edit of what the window shows, as <paramref name="input"/> in the trace, and writes it unless refused.</summary>
    public EditResult Edit(string input, Func<ThermiConfig, EditResult> edit)
    {
        _log.Input(input);
        var result = Problem is null ? edit(Config) : new EditResult(Config, Refusal.SettingsBroken);
        if (result.Refused is null)
        {
            _history.Record(Config, result.Config);
            Show(result.Config);
        }

        Say(result.Refused is { } refused ? new EditNote(refused, result.Problem, Skipped: false) : null);
        return result;
    }

    /// <summary>Says why a change the window could not even start was not made, as a refused edit does, and changes nothing.</summary>
    public EditResult Refuse(Refusal refused, string? problem = null)
    {
        Say(new EditNote(refused, problem, Skipped: false));
        return new EditResult(Config, refused, problem);
    }

    /// <summary>
    /// Renames a saved style, and with it the overlays that chose it and every undo step, so neither undo nor redo brings
    /// the old name back; the rename itself is no step of its own.
    /// </summary>
    public EditResult RenameStyle(SavedStyles saved, string from, string to)
    {
        _log.Input("rename style");
        var result = Problem is null ? saved.Rename(Config, _history, from, to) : new EditResult(Config, Refusal.SettingsBroken);
        if (result.Refused is null)
        {
            Show(result.Config);
        }

        Say(result.Refused is { } refused ? new EditNote(refused, result.Problem, Skipped: false) : null);
        return result;
    }

    public UndoResult? Undo()
    {
        EndGesture();
        return Step("undo", CanUndo ? _history.Undo(Config) : null);
    }

    public UndoResult? Redo()
    {
        EndGesture();
        return Step("redo", CanRedo ? _history.Redo(Config) : null);
    }

    /// <summary>Keeps the broken file beside it and writes the last settings read well, which then show as any outside change.</summary>
    public void Restore()
    {
        _log.Input("restore");
        _settings.Restore();
    }

    /// <summary>From here to <see cref="EndGesture"/>, as a drag from press to release, the edits are one undo step.</summary>
    public void BeginGesture()
    {
        _history.BeginGesture();
        _inGesture = true;
    }

    public void EndGesture()
    {
        if (!_inGesture)
        {
            return;
        }

        _history.EndGesture();
        _inGesture = false;
        if (_held)
        {
            _held = false;
            Outside();
        }
    }

    /// <summary>
    /// Takes a desktop gesture, the file as the overlay read it and as it wrote it, as a step of the window's history,
    /// whether its write has reached the window yet or not; a file that cannot be read makes no step.
    /// </summary>
    public void TakeGesture(string before, string after)
    {
        var (from, notRead) = ThermiConfigJson.Read(before);
        var (to, notWritten) = ThermiConfigJson.Read(after);
        if (notRead is null && notWritten is null)
        {
            _history.Record(from, to);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Detach() => _settings.Changed -= SettingsChanged;

    private UndoResult? Step(string input, UndoResult? result)
    {
        _log.Input(input);
        if (result is not null)
        {
            Show(result.Config);
            Say(result.Skipped > 0 ? new EditNote(null, null, Skipped: true) : null);
        }

        return result;
    }

    private void Say(EditNote? note)
    {
        Note = note;
        Noted?.Invoke(this, EventArgs.Empty);
    }

    private void Show(ThermiConfig config)
    {
        if (ConfigEdits.Replay(Config, config) is not { } change)
        {
            return;
        }

        Config = config;
        _settings.Edit(change);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SettingsChanged(object? sender, EventArgs e) => _onTurn(Outside);

    // The settings always hold the file then what the window wrote and still waits, so its own writes show as they were.
    private void Outside()
    {
        if (_inGesture)
        {
            _held = true;
            return;
        }

        Config = _settings.Config;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
