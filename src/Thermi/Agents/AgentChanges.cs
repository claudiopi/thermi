using System.Globalization;
using Thermi.Commands;
using Thermi.Core.Agents;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using Thermi.Overlay;

namespace Thermi.Agents;

/// <summary>
/// The commands that change something: the settings, through Core's edits written over the file as it is then, under its
/// mutex; the saved looks' files; the start at sign-in; and the fan finder's pick.
/// </summary>
internal sealed partial class AgentCommands
{
    // A write that finds the file's mutex taken waits out its timeout; three of them are a file someone keeps locked.
    private const int Tries = 3;

    /// <summary>Takes back the last edit this session made, where what it wrote is still there.</summary>
    public Task<AgentReply> UndoAsync(CancellationToken stop) =>
        OneAtATimeAsync(() => StepAsync(History.CanUndo ? found => History.Undo(found) : null, () => History.UndoName, History.CancelUndo), stop);

    public Task<AgentReply> RedoAsync(CancellationToken stop) =>
        OneAtATimeAsync(() => StepAsync(History.CanRedo ? found => History.Redo(found) : null, () => History.RedoName, History.CancelRedo), stop);

    private async Task<AgentReply> OneAtATimeAsync(Func<Task<AgentReply>> change, CancellationToken stop)
    {
        await _changing.WaitAsync(stop);
        try
        {
            return await change();
        }
        finally
        {
            _changing.Release();
        }
    }

    private async Task<AgentReply> EditAsync(string json, CancellationToken stop)
    {
        // The reading is taken before the edit waits its turn, so a service that does not answer holds up no other change.
        var (samples, _, _) = await AgentSamples.TakeAsync(environment.Sensors(), 1, time, stop);
        var snapshot = samples.Count > 0 ? samples[^1] : new Snapshot([]);
        return await OneAtATimeAsync(() => EditOfAsync(json, snapshot), stop);
    }

    private async Task<AgentReply> EditOfAsync(string json, Snapshot snapshot)
    {
        var settings = await SettingsAsync();
        Func<ThermiConfig, EditResult> edit;
        try
        {
            edit = AgentEdits.Of(json, snapshot, Texts.For(settings.Config.Language, CultureInfo.CurrentUICulture), environment.AltGrKeys);
            // Made once on the settings as read, so a field or value no edit has is a bad argument before the file is opened.
            edit(settings.Config);
        }
        catch (AgentInputException ex)
        {
            return new AgentReply(AgentExit.BadArguments, Error: ex.Message);
        }

        return await WriteAsync(edit);
    }

    private AgentReply Looks() => new(AgentExit.Done, AgentJson.Write(new LooksAnswer(Saved())));

    private async Task<AgentReply> LookAsync(AgentRequest request)
    {
        var saved = new SavedStyles(environment.StylesFolder);
        var name = request.Text ?? "";
        return request.Look switch
        {
            LookAction.Wear => saved.Named(name) is { } style
                ? await WriteAsync(config => StyleEdits.Wear(config, request.Overlay, style))
                : Refused(Refusal.StyleNotFound),
            LookAction.Save => await OfOverlayAsync(request.Overlay, style => Filed(() => saved.Save(style, name))),
            LookAction.Rename => await RenameAsync(saved, name, request.To ?? ""),
            LookAction.Delete => Filed(() => saved.Delete(name)),
            LookAction.Export => await ExportAsync(saved, request),
            LookAction.Import => await WriteAsync(config => StyleEdits.Import(config, request.Overlay ?? 0, name)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Look, "Unknown look action."),
        };
    }

    private async Task<AgentReply> ExportAsync(SavedStyles saved, AgentRequest request) => request.Text is { } name
        ? saved.Named(name) is { } style ? Exported(style) : Refused(Refusal.StyleNotFound)
        : await OfOverlayAsync(request.Overlay, Exported);

    private AgentReply Exported(StyleConfig style) =>
        new(AgentExit.Done, AgentJson.Write(new LooksAnswer(Saved()) { File = StyleFile.Write(style) }));

    // The file is renamed first, as the window renames it, and the overlays that wore it are renamed over the file as it is then.
    private async Task<AgentReply> RenameAsync(SavedStyles saved, string from, string to)
    {
        var settings = await SettingsAsync();
        if (settings.Problem is { } problem)
        {
            return Broken(problem);
        }

        var was = saved.Named(from)?.Name;
        EditResult renamed;
        try
        {
            renamed = saved.Rename(settings.Config, History, from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileRefused(ex);
        }

        if (renamed.Refused is { } refused)
        {
            return Refused(refused);
        }

        if (ConfigEdits.Replay(settings.Config, renamed.Config) is not { } replay)
        {
            return Done();
        }

        // The overlays that wore it could not follow the file's new name, so the file takes its old name back, as it was written.
        var written = await WriteAsync(config => new EditResult(replay(config)), keep: false);
        return written.Exit == AgentExit.Done || Filed(() => saved.Rename(renamed.Config, History, to.Trim(), was!).Refused).Exit == AgentExit.Done
            ? written
            : written with { Error = $"{written.Error} The look's file kept its new name, {to.Trim()}, while the overlays still wear {was}." };
    }

    private AgentReply StartWithWindows(bool? on)
    {
        if (environment.SensorsOnly())
        {
            return new AgentReply(
                AgentExit.Refused,
                AgentJson.Write(new StartAnswer(On: false, Overlay: false)),
                "This install is thermi's sensors alone: there is no overlay to start.");
        }

        if (on is { } set)
        {
            environment.Startup.Set(set);
        }

        return new AgentReply(AgentExit.Done, AgentJson.Write(new StartAnswer(environment.Startup.On, Overlay: true)));
    }

    // Core's edit, tried on the file as read and refused there without a write, then made over the file as it is then.
    private async Task<AgentReply> WriteAsync(Func<ThermiConfig, EditResult> edit, bool keep = true)
    {
        using var store = new ConfigStore(environment.SettingsPath, time);
        var read = await store.ReadAsync();
        if (read.Problem is { } problem)
        {
            return Broken(problem);
        }

        if (edit(read.Config) is { Refused: not null } refused)
        {
            return Edited(refused);
        }

        Exception? failed = null;
        for (var tried = 0; tried < Tries; tried++)
        {
            (ThermiConfig Before, EditResult After)? made = null;
            var written = await store.WriteAsync(found =>
            {
                made = (found, edit(found));
                return made.Value.After.Refused is null ? made.Value.After.Config : found;
            });
            if (written.Outcome == WriteOutcome.Broken)
            {
                return Broken(written.Problem);
            }

            failed = written.Error;

            if (written.Outcome == WriteOutcome.Written && made is var (before, after))
            {
                if (keep && after.Refused is null)
                {
                    History.Record(before, after.Config);
                }

                return Edited(after);
            }
        }

        return Unwritten(failed);
    }

    private static AgentReply Unwritten(Exception? failed) => new(
        AgentExit.SettingsBroken,
        Error: failed is IOException or UnauthorizedAccessException
            ? $"Windows refused to write the settings file; nothing was written: {failed.Message}"
            : "The settings file stayed locked by another program; nothing was written. Try again.");

    // A step taken but never written goes back where it was, so the next undo or redo is still that one.
    private async Task<AgentReply> StepAsync(Func<ThermiConfig, UndoResult?>? step, Func<StepName?> name, Action back)
    {
        if (step is null)
        {
            return new AgentReply(AgentExit.Done, AgentJson.Write(new EditAnswer(Done: false)));
        }

        var said = name() is { } named ? StepTexts.Of(named, new ValueFormat(Texts.English, CultureInfo.InvariantCulture)) : null;
        // The step is taken once, on the file as read, and what it changed is made again over the file as each write finds it.
        UndoResult? taken = null;
        Func<ThermiConfig, ThermiConfig>? replay = null;
        var reply = await WriteAsync(
            config =>
            {
                if (taken is null && step(config) is { } once)
                {
                    taken = once;
                    replay = ConfigEdits.Replay(config, once.Config);
                    return new EditResult(once.Config);
                }

                return new EditResult(replay?.Invoke(config) ?? config);
            },
            keep: false);
        if (reply.Exit != AgentExit.Done)
        {
            if (taken is not null)
            {
                back();
            }

            return reply;
        }

        // Done only where something was taken back: a step whose every change someone else has since changed takes nothing.
        return reply with { Json = AgentJson.Write(new EditAnswer(taken is { Applied: > 0 }) { Step = said }) };
    }

    // An overlay's look, the first's when none is named, from settings that read well: a broken file's defaults are no one's look.
    private async Task<AgentReply> OfOverlayAsync(int? overlay, Func<StyleConfig, AgentReply> then)
    {
        var settings = await SettingsAsync();
        var one = overlay ?? 0;
        return settings.Problem is { } problem ? Broken(problem)
            : one >= 0 && one < settings.Config.Overlays.Count ? then(settings.Config.Overlays[one].Style)
            : Refused(Refusal.NotFound);
    }

    private IReadOnlyList<string> Saved() => [.. new SavedStyles(environment.StylesFolder).List().Select(style => style.Name!)];

    // A saved look's file Windows refused to write or delete changes nothing, and says why.
    private static AgentReply Filed(Func<Refusal?> change)
    {
        try
        {
            return change() is { } refused ? Refused(refused) : Done();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileRefused(ex);
        }
    }

    private static AgentReply FileRefused(Exception ex) =>
        new(AgentExit.SettingsBroken, Error: $"Windows refused the saved look's file; nothing was changed: {ex.Message}");

    private static AgentReply Done() => new(AgentExit.Done, AgentJson.Write(new EditAnswer(Done: true)));

    private static AgentReply Refused(Refusal refusal) => Edited(new EditResult(new ThermiConfig(), refusal));

    private static AgentReply Edited(EditResult result)
    {
        var answer = AgentViews.Edited(result);
        return new AgentReply(answer.Done ? AgentExit.Done : AgentExit.Refused, AgentJson.Write(answer), answer.Message);
    }

    private static AgentReply Broken(ConfigProblem? problem) => new(
        AgentExit.SettingsBroken,
        Error: $"{AgentStatus.Said(AgentProblem.SettingsBroken).Cause} {problem?.OneLine(Texts.English, CultureInfo.InvariantCulture)}".TrimEnd());
}
