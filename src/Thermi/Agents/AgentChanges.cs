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
    public Task<AgentReply> UndoAsync() => StepAsync(History.CanUndo ? found => History.Undo(found) : null, () => History.UndoName);

    public Task<AgentReply> RedoAsync() => StepAsync(History.CanRedo ? found => History.Redo(found) : null, () => History.RedoName);

    private async Task<AgentReply> EditAsync(string json, CancellationToken stop)
    {
        var (samples, _, _) = await AgentSamples.TakeAsync(environment.Sensors(), 1, time, stop);
        var snapshot = samples.Count > 0 ? samples[^1] : new Snapshot([]);
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
            LookAction.Save => await StyleOfAsync(request.Overlay) is { } style
                ? Filed(() => saved.Save(style, name))
                : Refused(Refusal.NotFound),
            LookAction.Rename => await RenameAsync(saved, name, request.To ?? ""),
            LookAction.Delete => Filed(() => saved.Delete(name)),
            LookAction.Export => await ExportAsync(saved, request),
            LookAction.Import => await WriteAsync(config => StyleEdits.Import(config, request.Overlay ?? 0, name)),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Look, "Unknown look action."),
        };
    }

    private async Task<AgentReply> ExportAsync(SavedStyles saved, AgentRequest request)
    {
        var style = request.Text is { } name ? saved.Named(name) : await StyleOfAsync(request.Overlay ?? 0);
        return style is null
            ? Refused(request.Text is null ? Refusal.NotFound : Refusal.StyleNotFound)
            : new AgentReply(AgentExit.Done, AgentJson.Write(new LooksAnswer(Saved()) { File = StyleFile.Write(style) }));
    }

    // The file is renamed first, as the window renames it, and the overlays that wore it are renamed over the file as it is then.
    private async Task<AgentReply> RenameAsync(SavedStyles saved, string from, string to)
    {
        var settings = await SettingsAsync();
        if (settings.Problem is { } problem)
        {
            return Broken(problem);
        }

        var renamed = saved.Rename(settings.Config, History, from, to);
        return renamed.Refused is { } refused ? Refused(refused)
            : ConfigEdits.Replay(settings.Config, renamed.Config) is { } replay ? await WriteAsync(config => new EditResult(replay(config)), keep: false)
            : Done();
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

            if (written.Outcome == WriteOutcome.Written && made is var (before, after))
            {
                if (keep && after.Refused is null)
                {
                    History.Record(before, after.Config);
                }

                return Edited(after);
            }
        }

        return new AgentReply(AgentExit.Refused, Error: "The settings file stayed locked by another program; nothing was written. Try again.");
    }

    private async Task<AgentReply> StepAsync(Func<ThermiConfig, UndoResult?>? step, Func<StepName?> name)
    {
        if (step is null)
        {
            return new AgentReply(AgentExit.Done, AgentJson.Write(new EditAnswer(Done: false)));
        }

        var said = name() is { } named ? StepTexts.Of(named, new ValueFormat(Texts.English, CultureInfo.InvariantCulture)) : null;
        UndoResult? taken = null;
        var reply = await WriteAsync(config => new EditResult((taken ??= step(config))?.Config ?? config), keep: false);
        return reply.Exit == AgentExit.Done ? reply with { Json = AgentJson.Write(new EditAnswer(taken is not null) { Step = said }) } : reply;
    }

    private async Task<StyleConfig?> StyleOfAsync(int? overlay)
    {
        var config = (await SettingsAsync()).Config;
        return overlay is { } one && one >= 0 && one < config.Overlays.Count ? config.Overlays[one].Style : null;
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
            return new AgentReply(AgentExit.Refused, Error: $"Windows refused the saved look's file: {ex.Message}");
        }
    }

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
