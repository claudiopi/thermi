using Microsoft.Extensions.Time.Testing;
using Thermi.Core.Bridge;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using Thermi.Core.Translations;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// Undo and redo are operations by item id that never revert a change made outside the window.
/// </summary>
public sealed class UndoTests
{
    private static readonly ValueFormat InEnglish = new(Texts.English, System.Globalization.CultureInfo.GetCultureInfo("en-US"));

    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static ThermiConfig Everything => ThermiConfigJson.Read(TestFiles.Config("v6.json")).Config;

    // Each kind of edit, from the file with every field set; the overlay snapped is a copy, made first, so its row
    // undoes two steps.
    private static readonly Dictionary<string, Func<ThermiConfig, EditResult>[]> Edits = new(StringComparer.Ordinal)
    {
        ["Threshold"] = [config => ItemEdits.SetThreshold(config, "cpu.temperature", 75, 90)],
        ["MoveItem"] = [config => ItemEdits.Move(config, "/gpu-nvidia/0/power/0", 0)],
        ["AddSensor"] = [config => ItemEdits.AddSensor(config, 1, Desktop, "/gpu-nvidia/0/temperature/0")],
        ["ChangeSensor"] = [config => ItemEdits.ChangeSensor(config, "cpu.temperature-2", Desktop, "/amdcpu/0/temperature/3")],
        ["RenamePill"] = [config => ItemEdits.RenamePill(config, "ram.used", "MEM")],
        ["RenameSensor"] = [config => ThermiEdits.RenameSensor(config, Desktop, "/lpc/it8689e/0/temperature/1", "Chipset hub")],
        ["WarnByOfACombined"] = [config => ItemEdits.SetWarnBy(config, "ccd", Desktop, null)],
        ["WarnByOfMemory"] = [config => ItemEdits.SetWarnBy(config, "ram.used", Desktop, null)],
        ["GraphOfAnItem"] = [config => PowerEdits.SetGraph(config, "cpu.temperature", null)],
        ["GraphOfAnOverlay"] = [config => OverlayEdits.SetGraph(config, 1, GraphStyle.Line)],
        ["DeleteItem"] = [config => ItemEdits.Delete(config, "ccd")],
        ["Size"] = [config => OverlayEdits.SetSize(config, 1, 1.5)],
        ["Duplicate"] = [config => OverlayEdits.Duplicate(config, 1)],
        ["SnapTheCopy"] = [config => OverlayEdits.Duplicate(config, 1), config => OverlayEdits.Snap(config, 2, new SnapConfig(SnapSpot.TopLeft))],
        ["ThermiAndOverlayAtOnce"] =
        [
            config => ConfigEdits.Apply(
                config,
                current => WithOverlay(current with { Language = null }, 0, overlay => overlay with { Style = overlay.Style with { Gap = 12 } })),
        ],
        ["DeleteOverlay"] = [config => OverlayEdits.Delete(config, 0)],
        ["Reset"] = [OverlayEdits.Reset],
        ["AddOverlay"] = [OverlayEdits.Add],
    };

    public static TheoryData<string> EditKinds => new(Edits.Keys);

    [Theory]
    [MemberData(nameof(EditKinds))]
    public void EveryEditCanBeUndoneAndRedone(string kind)
    {
        var history = new UndoHistory();
        var states = new List<ThermiConfig> { Everything };
        foreach (var edit in Edits[kind])
        {
            states.Add(Done(history, states[^1], edit));
        }

        var undone = new List<UndoResult>();
        while (history.CanUndo)
        {
            undone.Add(history.Undo(undone.Count == 0 ? states[^1] : undone[^1].Config)!);
        }

        var redone = new List<UndoResult>();
        while (history.CanRedo)
        {
            redone.Add(history.Redo(redone.Count == 0 ? undone[^1].Config : redone[^1].Config)!);
        }

        Assert.Equal(states.Count, states.Select(File).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal([.. states.SkipLast(1).Reverse().Select(File)], undone.Select(result => File(result.Config)));
        Assert.Equal([.. states.Skip(1).Select(File)], redone.Select(result => File(result.Config)));
        Assert.All(undone.Concat(redone), result => Assert.Equal(0, result.Skipped));
        Assert.Equal(TestFiles.Config("v6.json").ReplaceLineEndings().TrimEnd(), File(undone[^1].Config).ReplaceLineEndings());
        Assert.Null(history.Redo(redone[^1].Config));
    }

    [Fact]
    public void OneGestureIsOneUndoStep()
    {
        var history = new UndoHistory();
        var config = new ThermiConfig();

        history.BeginGesture();
        foreach (var warning in new[] { 71, 74, 78, 81, 79 })
        {
            config = Done(history, config, current => ItemEdits.SetThreshold(current, "cpu.temperature", warning, 85));
        }

        history.EndGesture();
        var undone = history.Undo(config)!;

        Assert.Equal(new Threshold(90, 95), Item(undone.Config, "cpu.temperature").Threshold);
        Assert.Equal((1, 0), (undone.Applied, undone.Skipped));
        Assert.False(history.CanUndo);
        Assert.Null(history.Undo(undone.Config));
    }

    [Fact]
    public void UndoAndRedoNeverDuplicateOrOverwrite()
    {
        var history = new UndoHistory();
        var deleted = Done(history, new ThermiConfig(), config => ItemEdits.Delete(config, "gpu.load"));
        var sameId = ByHand(
            deleted,
            config => WithOverlay(
                config,
                0,
                overlay => overlay with { Items = [.. overlay.Items, new ItemConfig { Id = "gpu.load", Metric = Metrics.GpuTemperature.Id }] }));
        var resized = Done(history, sameId, config => OverlayEdits.SetSize(config, 0, 1.5));
        var sizeUndone = history.Undo(resized)!;
        var resizedByHand = ByHand(sizeUndone.Config, config => WithOverlay(config, 0, overlay => overlay with { Size = 1.25 }));

        var redone = history.Redo(resizedByHand)!;
        history.Undo(redone.Config);
        var deleteUndone = history.Undo(redone.Config)!;

        Assert.Equal((0, 1), (redone.Applied, redone.Skipped));
        Assert.Equal(1.25, redone.Config.Overlays[0].Size);
        Assert.Equal((0, 1), (deleteUndone.Applied, deleteUndone.Skipped));
        Assert.Equal(Metrics.GpuTemperature.Id, Assert.Single(deleteUndone.Config.Overlays[0].Items, item => item.Id == "gpu.load").Metric);
    }

    [Fact]
    public void UndoOfAddDeleteAndMoveRespectsHandEdits()
    {
        var history = new UndoHistory();
        var start = new ThermiConfig();
        var added = Done(history, start, config => ItemEdits.AddSensor(config, 0, Desktop, "/amdcpu/0/temperature/3"));
        var deleted = Done(history, added, config => ItemEdits.Delete(config, "cpu.load"));
        var moved = Done(history, deleted, config => ItemEdits.Move(config, "fan.cpu", 4));
        var byHand = ByHand(moved, config => WithItem(config, "/amdcpu/0/temperature/3", item => item with { Label = "CCD" }));

        var results = new[] { history.Undo(byHand)! }.ToList();
        results.Add(history.Undo(results[^1].Config)!);
        results.Add(history.Undo(results[^1].Config)!);

        Assert.Equal([.. Ids(start.Overlays[0]).Take(4), "/amdcpu/0/temperature/3", .. Ids(start.Overlays[0]).Skip(4)], Ids(results[^1].Config.Overlays[0]));
        Assert.Equal("CCD", Item(results[^1].Config, "/amdcpu/0/temperature/3").Label);
        Assert.Equal([0, 0, 1], results.Select(result => result.Skipped));
    }

    // Two overlays with every field changed: the reset leaves one default overlay, shown always, and keeps what is thermi's;
    // undone after a hand edit, every field still at its default comes back, and the hand edit stays.
    [Fact]
    public void AResetKeepsLanguageHotkeysAndTheTemperatureUnit()
    {
        var history = new UndoHistory();

        var reset = Done(history, Everything, OverlayEdits.Reset);
        var byHand = ByHand(reset, config => WithOverlay(config, 0, overlay => overlay with { Size = 1.5 }));
        var undone = history.Undo(byHand)!;

        Assert.Equal(File(new ThermiConfig()), File(new ThermiConfig { Overlays = reset.Overlays }));
        Assert.Equal((Everything.Language, Everything.Hotkeys, Everything.Temperature), (reset.Language, reset.Hotkeys, reset.Temperature));
        Assert.Equal(File(WithOverlay(Everything, 0, overlay => overlay with { Size = 1.5 })), File(undone.Config));
        Assert.Equal(1, undone.Skipped);
    }

    // Each edit is named by the field it changed and, when both ends have one, its value before and after, a line or an
    // offset in its reading's unit; an undo says the value it went back to, or a switch's state again. Changes of one
    // field within a second of each other are one step, and one after that a step of its own.
    [Fact]
    public void UndoStepsAreNamedAndGroupedWithinASecond()
    {
        var cpuPower = Texts.English.Of(MetricName.CpuPower);
        (Func<ThermiConfig, EditResult> Edit, ChangedField Field, string? Before, string? After, object? Back)[] edits =
        [
            (config => ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 90, 94), ChangedField.Critical, "95", "94 °C", "95 °C"),
            (config => ItemEdits.SetOwn(config, Metrics.CpuPower.Id, true), ChangedField.SeparatePill, null, null, UndoText.Off),
            (config => ItemEdits.RenamePill(config, Metrics.RamUsed.Id, "MEM"), ChangedField.Label, null, null, null),
            (config => ItemEdits.Move(config, Metrics.GpuLoad.Id, 0), ChangedField.Place, null, null, null),
            (config => ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 89, 95), ChangedField.Warning, "90", "89 °C", "90 °C"),
            (config => ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 80, 85), ChangedField.Thresholds, null, null, null),
            (
                config => ItemEdits.SetWarnBy(config, Metrics.CpuTemperature.Id, Desktop, new WarnByConfig { Metric = Metrics.CpuLoad.Id }),
                ChangedField.WarnBy,
                null,
                null,
                null),
            (config => PowerEdits.SetGraph(config, Metrics.CpuTemperature.Id, GraphStyle.Line), ChangedField.Graph, null, null, null),
            (
                config => OverlayEdits.SetGraph(config, 0, GraphStyle.None),
                ChangedField.Graph,
                Texts.English.Of(GraphStyle.Auto),
                Texts.English.Of(GraphStyle.None),
                Texts.English.Of(GraphStyle.Auto)),
            (config => PowerEdits.SetAlertOnly(config, Metrics.CpuTemperature.Id, true), ChangedField.AlertOnly, null, null, UndoText.Off),
            (
                config => new EditResult(WithItem(config, Metrics.CpuTemperature.Id, item => item with { Notify = true })),
                ChangedField.NotifyMe,
                null,
                null,
                UndoText.Off),
            (config => PowerEdits.SetOffset(config, Metrics.CpuTemperature.Id, Desktop, -5), ChangedField.Offset, "0", "-5 °C", "0 °C"),
            (config => PowerEdits.SetSmooth(config, Metrics.CpuLoad.Id, 10), ChangedField.Smoothing, "0", "10", "0"),
            (config => ItemEdits.Rename(config, Metrics.CpuPower.Id, " Package "), ChangedField.ItemName, cpuPower, "Package", cpuPower),
            (config => ItemEdits.AddSensor(config, 0, Desktop, "/gpu-nvidia/0/temperature/0"), ChangedField.ItemAdded, null, null, null),
            (config => ItemEdits.Delete(config, Metrics.CpuPower.Id), ChangedField.ItemRemoved, null, null, null),
            (config => OverlayEdits.Rename(config, 0, "Desk"), ChangedField.OverlayName, null, "Desk", null),
            (config => OverlayEdits.SetHidden(config, 0, hidden: true), ChangedField.Hidden, null, null, null),
            (OverlayEdits.Add, ChangedField.OverlayAdded, null, null, null),
            (config => ThermiEdits.SetTemperature(config, TemperatureUnit.Fahrenheit), ChangedField.TemperatureUnit, null, null, null),

            // Cold review 4 (Q10): a choice is named by its words, so an undo says which it went back to.
            (
                config => StyleEdits.Restyle(config, 0, style => style with { Pills = PillTheme.Dark }),
                ChangedField.Theme,
                Texts.English.Of(PillTheme.System),
                Texts.English.Of(PillTheme.Dark),
                Texts.English.Of(PillTheme.System)),
            (config => OverlayEdits.Resize(config, 0, 1.5, Orientation.Vertical), ChangedField.Size, null, null, null),
            (config => OverlayEdits.MoveItem(config, Metrics.GpuLoad.Id, null), ChangedField.OverlayAdded, null, null, null),
        ];
        var time = new FakeTimeProvider();

        foreach (var (edit, field, before, after, undone) in edits)
        {
            var history = new UndoHistory(time);
            var done = Done(history, new ThermiConfig(), edit);
            var undoName = history.UndoName!;
            var offered = WindowFacts.Steps(history.UndoName, history.RedoName, InEnglish);
            var restored = history.Undo(done)!.Config;
            var shown = WindowFacts.UndoneOf(undoName, done, restored, InEnglish);
            // QA round 5 (P7): an item added or removed is named with its name, as "Add item “GPU temperature”".
            var named = field == ChangedField.ItemAdded ? Say(UndoText.Named, Say(field), Texts.English.Of(MetricName.GpuTemperature))
                : field == ChangedField.ItemRemoved ? Say(UndoText.Named, Say(field), cpuPower)
                : Say(field);

            Assert.Equal(new UndoSteps(StepTexts.Of(undoName, InEnglish), null), offered);
            Assert.Equal(new UndoSteps(null, StepTexts.Of(undoName, InEnglish)), WindowFacts.Steps(history.UndoName, history.RedoName, InEnglish));
            // The window shows what the undo changed back: an item still there, else an overlay, never both.
            Assert.True(shown.Item is null || restored.Overlays.Any(overlay => overlay.Items.Any(item => item.Id == shown.Item)));
            Assert.True(shown.Item is null || shown.Overlay is null);
            Assert.Equal(StepTexts.Undone(undoName, InEnglish), shown.Said);
            Assert.Equal(undoName, history.RedoName);
            Assert.Equal(
                Say(UndoText.Undo, before is null ? named : Say(UndoText.Change, named, before, after!)),
                StepTexts.Button(UndoText.Undo, undoName, InEnglish));
            Assert.Equal(
                undone switch
                {
                    UndoText state => Say(UndoText.Again, named, Say(state)),
                    string value => Say(UndoText.BackTo, named, value),
                    _ => Say(UndoText.Undone, named),
                },
                StepTexts.Undone(undoName, InEnglish));

            // A redo says what it did again, the switch's other state or the value after.
            var redone = history.Redo(restored)!.Config;
            Assert.Equal(
                undone switch
                {
                    UndoText state => Say(UndoText.RedoneAgain, named, Say(state == UndoText.On ? UndoText.Off : UndoText.On)),
                    string => Say(UndoText.RedoneTo, named, after!),
                    _ => Say(UndoText.Redone, named),
                },
                WindowFacts.UndoneOf(undoName, restored, redone, InEnglish, redo: true).Said);
        }

        // Hiding is an action, so showing the overlay again is named by its own button.
        Assert.Equal(Say(UndoText.Undone, Say(UiText.ShowThisOverlay)), StepTexts.Undone(new StepName(ChangedField.Hidden, true, false), InEnglish));

        // In °F, a line is a temperature and an offset a difference of two.
        var fahrenheit = InEnglish with { Temperature = TemperatureUnit.Fahrenheit };
        Assert.Equal(Say(UndoText.Change, Say(ChangedField.Critical), "203", "201 °F"), StepTexts.Of(Named(edits[0].Edit), fahrenheit));
        Assert.Equal(Say(UndoText.Change, Say(ChangedField.Offset), "0", "-9 °F"), StepTexts.Of(Named(edits[11].Edit), fahrenheit));

        var grouped = new UndoHistory(time);
        var config = new ThermiConfig();
        foreach (var (critical, after) in new[] { (96, 0.3), (97, 0.3), (98, 1.5), (99, 0) })
        {
            config = Done(grouped, config, current => ItemEdits.SetThreshold(current, Metrics.CpuTemperature.Id, 90, critical));
            time.Advance(TimeSpan.FromSeconds(after));
        }

        var first = grouped.Undo(config)!;
        Assert.Equal(new StepName(ChangedField.Critical, 95.0, 98.0, SensorKind.Temperature), grouped.UndoName);
        Assert.Equal(98, Item(first.Config, Metrics.CpuTemperature.Id).Threshold!.Critical);
        Assert.Equal(95, Item(grouped.Undo(first.Config)!.Config, Metrics.CpuTemperature.Id).Threshold!.Critical);

        // A drag right after a key on the same line is a step of its own, and so is the key right after it.
        var dragged = new UndoHistory(time);
        var keyed = Done(dragged, new ThermiConfig(), current => ItemEdits.SetThreshold(current, Metrics.CpuTemperature.Id, 90, 96));
        dragged.BeginGesture();
        var held = Done(dragged, keyed, current => ItemEdits.SetThreshold(current, Metrics.CpuTemperature.Id, 90, 99));
        dragged.EndGesture();
        var keyedAgain = Done(dragged, held, current => ItemEdits.SetThreshold(current, Metrics.CpuTemperature.Id, 90, 100));
        var afterKey = dragged.Undo(keyedAgain)!.Config;
        Assert.Equal(
            (99, 96),
            (
                Item(afterKey, Metrics.CpuTemperature.Id).Threshold!.Critical,
                Item(dragged.Undo(afterKey)!.Config, Metrics.CpuTemperature.Id).Threshold!.Critical));

        // A field changed back within a second leaves no step; another field between them keeps both.
        var back = new UndoHistory(time);
        var sized = Done(back, new ThermiConfig(), current => OverlayEdits.SetSize(current, 0, 1.5));
        var unsized = Done(back, sized, current => OverlayEdits.SetSize(current, 0, 1));
        var canUndo = back.CanUndo;
        var unit = Done(back, unsized, current => ThermiEdits.SetTemperature(current, TemperatureUnit.Fahrenheit));
        var resized = Done(back, unit, current => OverlayEdits.SetSize(current, 0, 1.25));
        var celsius = Done(back, resized, current => ThermiEdits.SetTemperature(current, TemperatureUnit.Celsius));
        Done(back, celsius, current => ThermiEdits.SetTemperature(current, TemperatureUnit.Fahrenheit));
        Assert.Equal((false, new StepName(ChangedField.Size, 1.0, 1.25)), (canUndo, back.UndoName));

        // Items that only change overlay are named by what happened to the overlays: a merge, or a move.
        var apart = Done(OverlayEdits.MoveItem(new ThermiConfig(), Metrics.GpuLoad.Id, null));
        var merged = new UndoHistory(time);
        Done(merged, apart, current => OverlayEdits.Merge(current, 1, 0));
        var moved = new UndoHistory(time);
        Done(moved, Done(OverlayEdits.MoveItem(apart, Metrics.CpuLoad.Id, 1)), current => OverlayEdits.MoveItem(current, Metrics.CpuLoad.Id, 0));
        Assert.Equal((ChangedField.Merge, ChangedField.Place), (merged.UndoName!.Field, moved.UndoName!.Field));
    }

    private static string Say<T>(T text, params object[] values)
        where T : struct, Enum => Texts.English.Format(InEnglish.Numbers, text, values);

    // The name of the step an edit of the default settings makes.
    private static StepName Named(Func<ThermiConfig, EditResult> edit)
    {
        var history = new UndoHistory();
        Done(history, new ThermiConfig(), edit);
        return history.UndoName!;
    }
}
