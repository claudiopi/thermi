using Thermi.Core.Editing;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Rules;

/// <summary>The rules of undo for each kind of change, when the file changed by hand in between.</summary>
public sealed class UndoRulesTests
{
    private static Snapshot Desktop => TestRecordings.Read(TestRecordings.Admin);

    private static ThermiConfig TwoOverlays => Done(OverlayEdits.Add(new ThermiConfig()));

    /// <summary>
    /// The settings the window starts from, its edit, the change made by hand over it, what must hold after the undo, and
    /// how many of the undo's changes are skipped, when that is part of the rule.
    /// </summary>
    private sealed record HandEdit(
        Func<ThermiConfig> Start,
        Func<ThermiConfig, EditResult> Edit,
        Func<ThermiConfig, ThermiConfig> Hand,
        Action<ThermiConfig, UndoResult> Holds,
        int? Skipped = 1);

    private static readonly Dictionary<string, HandEdit> HandEdits = new(StringComparer.Ordinal)
    {
        ["a threshold set again"] = new(
            () => new ThermiConfig(),
            config => ItemEdits.SetThreshold(config, Metrics.CpuTemperature.Id, 75, 85),
            config => WithItem(config, Metrics.CpuTemperature.Id, item => item with { Threshold = new Threshold(80, 85) }),
            (_, undone) => Assert.Equal((new Threshold(80, 85), 0), (Item(undone.Config, Metrics.CpuTemperature.Id).Threshold, undone.Applied))),
        ["a drag after a snap"] = new(
            () => new ThermiConfig(),
            config => OverlayEdits.Snap(config, 0, new SnapConfig(SnapSpot.BottomLeft)),
            config => WithOverlay(config, 0, overlay => overlay with { Snap = null, Position = new Position(640, 380) }),
            (_, undone) => Assert.Equal((false, new Position(640, 380)), (undone.Config.Overlays[0].Snapped is not null, undone.Config.Overlays[0].Position))),
        ["the item deleted"] = new(
            () => new ThermiConfig(),
            config => ItemEdits.RenamePill(config, Metrics.GpuLoad.Id, "VIDEO"),
            config => WithOverlay(config, 0, overlay => overlay with { Items = [.. overlay.Items.Where(item => item.Id != Metrics.GpuLoad.Id)] }),
            (byHand, undone) => Assert.Equal((File(byHand), 0), (File(undone.Config), undone.Applied))),
        ["the item moved again"] = new(
            () => new ThermiConfig(),
            config => ItemEdits.Move(config, Metrics.CpuFan.Id, 4),
            config => WithOverlay(
                config,
                0,
                overlay => overlay with { Items = [.. overlay.Items.Where(item => item.Id != Metrics.CpuFan.Id), Item(config, Metrics.CpuFan.Id)] }),
            (_, undone) => Assert.Equal(Metrics.CpuFan.Id, undone.Config.Overlays[0].Items[^1].Id)),
        ["the overlay of a deleted item deleted"] = new(
            () => TwoOverlays,
            config => ItemEdits.Delete(config, "gpu.load-2"),
            config => config with { Overlays = [config.Overlays[0]] },
            (_, undone) => Assert.Equal([.. Ids(TwoOverlays.Overlays[0]), "gpu.load-2"], Ids(Assert.Single(undone.Config.Overlays))),
            Skipped: null),
        ["an item of an overlay reset that shares none"] = new(
            () => Done(ItemEdits.AddSensor(
                new ThermiConfig { Overlays = [new OverlayConfig { Size = 1.25, Items = [] }] },
                0,
                Desktop,
                "/amdcpu/0/temperature/3")),
            OverlayEdits.Reset,
            config => WithItem(config, Metrics.CpuLoad.Id, item => item with { Label = "LOAD" }),
            (_, undone) => Assert.Equal(
                (1.25, "/amdcpu/0/temperature/3," + Metrics.CpuLoad.Id),
                (undone.Config.Overlays[0].Size, string.Join(',', Ids(undone.Config.Overlays[0]))))),
        ["an added overlay resized"] = new(
            () => new ThermiConfig(),
            OverlayEdits.Add,
            config => WithOverlay(config, 1, overlay => overlay with { Size = 1.5 }),
            (_, undone) => Assert.Equal(2, undone.Config.Overlays.Count)),
        ["a deleted overlay made again"] = new(
            () => ByHand(TwoOverlays, config => WithOverlay(config, 1, overlay => overlay with { Size = 1.5 })),
            config => OverlayEdits.Delete(config, 1),
            config => WithOverlay(
                config,
                0,
                overlay => overlay with { Items = [.. overlay.Items, new ItemConfig { Id = "ram.used-2", Metric = Metrics.RamLoad.Id }] }),
            (_, undone) => Assert.Single(undone.Config.Overlays)),
    };

    // Whatever the window changed, a change made by hand over it after the edit stays when the edit is undone.
    [Theory]
    [InlineData("a threshold set again")]
    [InlineData("a drag after a snap")]
    [InlineData("the item deleted")]
    [InlineData("the item moved again")]
    [InlineData("the overlay of a deleted item deleted")]
    [InlineData("an item of an overlay reset that shares none")]
    [InlineData("an added overlay resized")]
    [InlineData("a deleted overlay made again")]
    public void UndoKeepsWhatWasChangedByHand(string change)
    {
        var (start, edit, hand, holds, skipped) = HandEdits[change];
        var history = new UndoHistory();
        var byHand = ByHand(Done(history, start(), edit), hand);

        var undone = history.Undo(byHand)!;

        holds(byHand, undone);
        Assert.True(skipped is null || skipped == undone.Skipped, $"{undone.Skipped} changes skipped, not {skipped}");
    }

    // A property no field of ConfigFields tracks makes no undo step: the window could change it and never take it back.
    [Fact]
    public void EverySettingCanBeUndone()
    {
        var everything = ThermiConfigJson.Read(TestFiles.Config("v3.json")).Config;
        var item = new ItemConfig { Id = "item", Metric = Metrics.CpuLoad.Id };
        var full = new ItemConfig
        {
            Name = "Everything",
            Metric = Metrics.GpuLoad.Id,
            Sensor = "/a",
            Sensors = ["/b", "/c"],
            Combine = Combine.Sum,
            Auto = Metrics.CpuLoad.Id,
            Label = "ALL",
            Own = true,
            Tray = true,
            Color = "#FFFFFF",
            Meter = new MeterConfig { Metric = Metrics.CpuLoad.Id },
            Threshold = new Threshold(10, 20),
            Offset = 1,
            Stat = ItemStat.Max,
            Smooth = 3,
            Decimals = 2,
            Bits = true,
            Graph = true,
            AlertOnly = true,
            Notify = true,
        };
        var overlay = new OverlayConfig { Items = [item] };

        AssertNone(
            Untracked(new ThermiConfig { Overlays = [overlay] }, everything, config => config, ["Version", "Overlays"])
            .Concat(Untracked(
                overlay,
                everything.Overlays[0] with { Position = everything.Overlays[1].Position },
                each => new ThermiConfig { Overlays = [each] },
                ["Items"]))
            .Concat(Untracked(overlay.Style, everything.Overlays[0].Style, style => new ThermiConfig { Overlays = [overlay with { Style = style }] }, []))
            .Concat(Untracked(item, full, each => new ThermiConfig { Overlays = [overlay with { Items = [each] }] }, ["Id"])));
    }

    // Each settable property in turn takes the donor's value; undoing that change must give the start back.
    private static IEnumerable<string> Untracked<T>(T start, T donor, Func<T, ThermiConfig> settings, string[] excluded)
        where T : class
    {
        var clone = typeof(T).GetMethod("<Clone>$")!;
        foreach (var property in typeof(T).GetProperties().Where(each => each.SetMethod is not null && !excluded.Contains(each.Name, StringComparer.Ordinal)))
        {
            var changed = (T)clone.Invoke(start, null)!;
            property.SetValue(changed, property.GetValue(donor));
            var history = new UndoHistory();
            history.Record(settings(start), settings(changed));
            if (File(settings(start)) == File(settings(changed)))
            {
                yield return $"{typeof(T).Name}.{property.Name} holds the same value in the test's donor: give it another";
            }
            else if (history.Undo(settings(changed)) is not { } undone || File(undone.Config) != File(settings(start)))
            {
                yield return $"{typeof(T).Name}.{property.Name} cannot be undone: add it to ConfigFields";
            }
        }
    }

    private static void AssertNone(IEnumerable<string> findings)
    {
        var all = findings.ToList();
        Assert.True(all.Count == 0, string.Join(Environment.NewLine, all));
    }

    [Fact]
    public void AnEditThatChangesNothingIsNoStep()
    {
        var history = new UndoHistory();
        var config = new ThermiConfig();

        history.Record(config, Done(ItemEdits.Move(config, Metrics.CpuTemperature.Id, -3)));
        history.BeginGesture();
        history.EndGesture();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    // The window writes what it changed over what the file holds then: what another process wrote meanwhile stays, the
    // same field included.
    [Fact]
    public void AReplayedEditKeepsWhatChangedMeanwhile()
    {
        var before = new ThermiConfig();
        var after = Done(ItemEdits.SetThreshold(Done(ItemEdits.Move(before, Metrics.RamUsed.Id, 0)), Metrics.CpuTemperature.Id, 75, 90));
        var relabeled = ByHand(before, config => WithItem(config, Metrics.GpuLoad.Id, item => item with { Label = "CARD" }));
        var rethresholded = ByHand(before, config => WithItem(config, Metrics.CpuTemperature.Id, item => item with { Threshold = new Threshold(80, 95) }));

        var replay = ConfigEdits.Replay(before, after)!;

        Assert.Equal(File(after), File(replay(before)));
        Assert.Equal(File(ByHand(after, config => WithItem(config, Metrics.GpuLoad.Id, item => item with { Label = "CARD" }))), File(replay(relabeled)));
        Assert.Equal(new Threshold(80, 95), Item(replay(rethresholded), Metrics.CpuTemperature.Id).Threshold);
        Assert.Equal(Metrics.RamUsed.Id, replay(rethresholded).Overlays[0].Items[0].Id);
        Assert.Null(ConfigEdits.Replay(before, Done(ItemEdits.Move(before, Metrics.CpuTemperature.Id, 0))));
    }
}
