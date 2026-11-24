using System.Globalization;
using System.Text.Json;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Sensors;
using Thermi.Core.Settings;
using static Thermi.Tests.Acceptance.Edited;

namespace Thermi.Tests.Acceptance;

/// <summary>
/// A style is the look alone, saved, exported and imported as one small file.
/// </summary>
public sealed class SavedStyleTests
{
    private static StyleConfig Night => ThermiConfigJson.Read(TestFiles.Config("v4.json")).Config.Overlays[0].Style;

    private static List<string?> Names(SavedStyles styles) => [.. styles.List().Select(style => style.Name)];

    [Fact]
    public void AStyleIsTheLookAlone()
    {
        var (config, problem) = ThermiConfigJson.Read(TestFiles.Config("v4.json"));

        var style = config.Overlays[0].Style;

        Assert.Null(problem);
        Assert.Equal(
            [
                "Colors", "Corners", "Effect", "Font", "Gap", "Label", "Layout", "Name",
                "NoLabels", "Opacity", "Orientation", "Pills", "Preset", "Units", "Value",
            ],
            typeof(StyleConfig).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            new StyleConfig(Gap: 4, Units: false)
            {
                Name = "Night",
                Layout = PillLayout.Each,
                Orientation = Orientation.Vertical,
                NoLabels = true,
                Preset = StylePreset.Custom,
                Label = new TextStyle(12.5, TextWeight.Regular),
                Value = new TextStyle(14, TextWeight.Bold),
                Opacity = new OpacityConfig(0.4, 0.9),
                Pills = PillTheme.Light,
                Colors = new StyleColors { Normal = "#0072B2", Warning = "#E69F00", Critical = "#D55E00", Label = "#5C5C5A", Value = "#1A1A1A" },
                Font = StyleFont.Bahnschrift,
                Corners = CornerStyle.Soft,
                Effect = TextEffect.Outline,
            },
            style);

        // Worn on another overlay, the look keeps that overlay's layout, direction and labels, which change no look and keep its name.
        var worn = Done(StyleEdits.Choose(new ThermiConfig(), 0, style));
        Assert.Equal(style with { Layout = PillLayout.Pills, Orientation = Orientation.Horizontal, NoLabels = false }, worn.Overlays[0].Style);
        Assert.Equal(
            "Night",
            Done(StyleEdits.Restyle(worn, 0, each => each with { Layout = PillLayout.One, Orientation = Orientation.Vertical, NoLabels = true }))
                .Overlays[0].Style.Name);
        Assert.Equal("Night", SavedStyles.Chosen(worn.Overlays[0].Style, [style])?.Name);

        // Saved or exported, the look holds no layout, direction or labels, and one written with them before reads without them.
        var file = StyleFile.Write(style);
        var older = file.Replace("\"style\": {", "\"style\": { \"layout\": \"each\", \"orientation\": \"vertical\",", StringComparison.Ordinal);
        Assert.All(["layout", "orientation", "noLabels"], word => Assert.DoesNotContain(word, file, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(style.WornOn(new StyleConfig()), StyleFile.Read(older).Style);
    }

    [Fact]
    public void SavedStylesCanBeRenamedAndDeletedWithoutChangingTheOverlay()
    {
        using var folder = new StyleFolder();
        var styles = new SavedStyles(folder.Path);
        var custom = Done(StyleEdits.SetPreset(new ThermiConfig(), 0, StylePreset.Custom));
        var look = custom.Overlays[0].Style;

        Refusal?[] saved = [styles.Save(look, " "), styles.Save(look, new string('n', 41)), styles.Save(look, " Night "), styles.Save(look, "night")];
        // One past a style file's size, as an import would refuse it, is never listed.
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder.Path, "Huge.thermi-style"), new string(' ', StyleFile.MaxLength + 1));
        var renamed = Done(styles.Rename(custom, new UndoHistory(), "night", "Dusk"));
        var listed = Names(styles);
        var chosen = Done(StyleEdits.Choose(renamed, 0, styles.List()[0]));
        var shownBefore = SavedStyles.Chosen(chosen.Overlays[0].Style, styles.List());

        // An imported style that takes a saved one's name but not its look is not that style.
        var sameNameOtherLook = SavedStyles.Chosen(chosen.Overlays[0].Style with { Name = "dusk", Gap = 2 }, styles.List());
        var sameNameOtherCase = SavedStyles.Chosen(chosen.Overlays[0].Style with { Name = "dusk" }, styles.List());
        var deleted = styles.Delete("dusk");

        Assert.Equal([Refusal.StyleNameEmpty, Refusal.StyleNameTooLong, null, Refusal.StyleNameTaken], saved);
        Assert.Equal(["Dusk"], listed);
        Assert.Equal(File(custom), File(renamed));
        Assert.Equal(look with { Name = "Dusk" }, chosen.Overlays[0].Style);
        Assert.Equal("Dusk", shownBefore?.Name);
        Assert.Null(sameNameOtherLook);
        Assert.Equal("Dusk", sameNameOtherCase?.Name);
        Assert.Null(deleted);
        Assert.Empty(styles.List());
        Assert.Null(SavedStyles.Chosen(chosen.Overlays[0].Style, styles.List()));
        Assert.Null(SavedStyles.Chosen(look, [look with { Name = "Dusk" }]));
        Assert.Equal(Refusal.StyleNotFound, styles.Delete("Dusk"));
        Assert.Equal(Refusal.StyleNotFound, styles.Rename(custom, new UndoHistory(), "Dusk", "Dawn").Refused);
    }

    [Fact]
    public void ChoosingASavedStyleIsUndoneButSavingIsNot()
    {
        using var folder = new StyleFolder();
        var styles = new SavedStyles(folder.Path);
        var history = new UndoHistory();
        var before = new ThermiConfig();
        styles.Save(new StyleConfig { Pills = PillTheme.Light, Preset = StylePreset.Large }, "Day");
        styles.Save(new StyleConfig(Gap: 2), "Tight");

        var chosen = Done(history, before, config => StyleEdits.Choose(config, 0, styles.List()[0]));
        styles.Delete("Tight");
        var undone = history.Undo(chosen)!;

        Assert.Equal(("Day", StylePreset.Large), (chosen.Overlays[0].Style.Name, chosen.Overlays[0].Style.Preset));
        Assert.Equal(File(before), File(undone.Config));
        Assert.Equal(0, undone.Skipped);
        Assert.False(history.CanUndo);
        Assert.Equal(["Day"], Names(styles));
        Assert.Equal(Refusal.NotFound, StyleEdits.Choose(before, 1, styles.List()[0]).Refused);
    }

    [Fact]
    public void AnyNameMakesAFile()
    {
        using var folder = new StyleFolder();
        var styles = new SavedStyles(folder.Path);
        string[] names = ["a<b>c:d\"e/f\\g|h?i*", "CON", "com1.old", "Night.", "a?b", "a*b", "Ñandú ☾", "tab\there"];

        var saved = names.Select(name => styles.Save(new StyleConfig { Pills = PillTheme.Light }, name)).ToList();
        var files = Directory.GetFiles(folder.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        Assert.All(saved, refused => Assert.Null(refused));
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), Names(styles));
        Assert.Equal(names.Length, files.Count);
        Assert.Equal(
            [
                "Night_.thermi-style",
                "_CON.thermi-style",
                "_com1.old.thermi-style",
                "a_b 2.thermi-style",
                "a_b.thermi-style",
                "a_b_c_d_e_f_g_h_i_.thermi-style",
                "tab_here.thermi-style",
                "Ñandú ☾.thermi-style",
            ],
            files);
        Assert.Equal("COM10.thermi-style", SavedStyles.FileNameOf("COM10"));
    }

    [Fact]
    public void AnExportedStyleHoldsOnlyTheLook()
    {
        var exported = StyleFile.Write(Night);

        using var file = JsonDocument.Parse(exported);

        Assert.Equal(["thermiStyle", "style"], file.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, file.RootElement.GetProperty("thermiStyle").GetInt32());
        Assert.Equal("Night", file.RootElement.GetProperty("style").GetProperty("name").GetString());
        Assert.All(
            ["items", "sensor", "metric", "snap", "position", "monitor", "language", "/amdcpu", "/lpc", ":\\"],
            word => Assert.DoesNotContain(word, exported, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Night.WornOn(new StyleConfig()), StyleFile.Read(exported).Style);
    }

    // A style holds no sensor, so one made on another PC shows every item of this one's, without admin rights too.
    [Fact]
    public void AnImportedStyleAppliesAtOnceAndUndoesInOneStep()
    {
        using var folder = new StyleFolder();
        var styles = new SavedStyles(folder.Path);
        var history = new UndoHistory();
        var before = new ThermiConfig();
        var file = StyleFile.Write(Night);
        var pasted = "\r\n  " + file.ReplaceLineEndings("\r\n") + "\n";

        foreach (var text in new[] { file, pasted, file.PadRight(StyleFile.MaxLength) })
        {
            var imported = Done(history, before, config => StyleEdits.Import(config, 0, text));
            var undone = history.Undo(imported)!;

            Assert.Equal((File(before), 0), (File(undone.Config), undone.Skipped));
            Assert.False(history.CanUndo);
            Assert.Equal(Night.WornOn(before.Overlays[0].Style), imported.Overlays[0].Style);
            Assert.Equal(Ids(before.Overlays[0]), Ids(imported.Overlays[0]));
            Assert.Equal(
                before.Overlays[0].Items.Count,
                OverlayLayout.Build(
                    imported.Overlays[0],
                    SensorMap.Read(TestRecordings.Read(TestRecordings.NoAdmin), imported.Overlays[0]),
                    CultureInfo.InvariantCulture).Count);
        }

        Assert.Null(styles.Save(Night, Night.Name!));
        Assert.Equal(["Night"], Names(styles));
        Assert.Equal(Refusal.StyleNameTaken, styles.Save(Night, Night.Name!));
    }

    [Fact]
    public void ABrokenNewerOrOutOfRangeStyleIsRefused()
    {
        var before = new ThermiConfig();
        string[] files =
        [
            """{ "thermiStyle": 1, "style": { "gap": 4 """,
            """{ "thermiStyle": 1, "style": { "gap": "wide" } }""",
            """{ "thermiStyle": 2, "style": {} }""",
            """{ "thermiStyle": 1, "style": { "preset": "custom", "label": { "size": 30, "weight": "bold" } } }""",
            """{ "thermiStyle": 1, "style": { "opacity": { "background": 5 } } }""",
            """{ "thermiStyle": 1, "style": { "opacity": { "overlay": 0.1 } } }""",
            """{ "thermiStyle": 1, "style": { "colors": { "normal": "red" } } }""",
            """{ "style": {} }""",
            """[]""",
            new string(' ', StyleFile.MaxLength + 1),
        ];

        var results = files.Select(text => StyleEdits.Import(before, 0, text)).ToList();

        Assert.Equal(
            [Refusal.BrokenStyle, Refusal.BrokenStyle, Refusal.NewerStyle, .. Enumerable.Repeat(Refusal.BrokenStyle, 6), Refusal.StyleTooLarge],
            results.Select(result => result.Refused));
        Assert.All(results, result => Assert.Same(before, result.Config));
        Assert.All(
            results.Zip(
            [
                "LineNumber: 0",
                "$.style.gap",
                "thermiStyle is 2",
                "style.label.size",
                "style.opacity.background",
                "style.opacity.overlay",
                "style.colors.normal",
                "thermiStyle",
                "not a JSON object",
                "more than 1048576",
            ]),
            pair => Assert.Contains(pair.Second, pair.First.Problem ?? "", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownFieldsAreIgnoredAndMissingOnesTakeTheirDefault()
    {
        var before = new ThermiConfig();

        var unknown = Done(StyleEdits.Import(before, 0, """{ "thermiStyle": 1, "author": "me", "style": { "gap": 4, "sparkle": true } }""")).Overlays[0].Style;
        var noOpacity = Done(StyleEdits.Import(before, 0, """{ "thermiStyle": 1, "style": { "pills": "light", "opacity": null } }""")).Overlays[0].Style;
        var noStyle = Done(StyleEdits.Import(before, 0, """{ "thermiStyle": 1 }""")).Overlays[0].Style;

        Assert.Equal(new StyleConfig(Gap: 4), unknown);
        Assert.Equal(new StyleConfig { Pills = PillTheme.Light }, noOpacity);
        Assert.Equal(new OpacityConfig(), noOpacity.Opacity);
        Assert.Equal(new StyleConfig(), noStyle);
    }

    [Fact]
    public void AStyleForgetsItsNameWhenTheLookChanges()
    {
        var chosen = new ThermiConfig { Overlays = [new OverlayConfig { Style = new StyleConfig { Name = "Night", Preset = StylePreset.Large } }] };

        var resized = Done(StyleEdits.SetText(chosen, 0, TextPart.Value, new TextStyle(18, TextWeight.Bold)));
        var recolored = Done(StyleEdits.SetColor(chosen, 0, StyleColor.Value, "#FFFFFF"));
        var preset = Done(StyleEdits.SetPreset(chosen, 0, StylePreset.Compact));

        Assert.All([resized, recolored, preset], changed => Assert.Null(changed.Overlays[0].Style.Name));
        Assert.Equal("Night", Done(ItemEdits.SetOwn(chosen, Metrics.CpuPower.Id, true)).Overlays[0].Style.Name);

        // A change to what the style already has leaves the look as it was, so it is still the saved style.
        var samePreset = Done(StyleEdits.SetPreset(chosen, 0, StylePreset.Large));
        var sameOpacity = Done(StyleEdits.SetOpacity(chosen, 0, chosen.Overlays[0].Style.Opacity));
        Assert.All([samePreset, sameOpacity], same => Assert.Equal("Night", same.Overlays[0].Style.Name));
    }

    [Fact]
    public void RenamingTheChosenStyleRenamesItInTheSettings()
    {
        using var folder = new StyleFolder();
        var styles = new SavedStyles(folder.Path);
        var history = new UndoHistory();
        var before = new ThermiConfig();
        styles.Save(new StyleConfig { Pills = PillTheme.Light }, "Night");
        styles.Save(new StyleConfig(Gap: 2), "Dawn");

        var own = Done(history, before, config => ItemEdits.SetOwn(config, Metrics.CpuPower.Id, true));
        var chosen = Done(history, own, config => StyleEdits.Choose(config, 0, styles.List().Single(style => style.Name == "Night")));
        var duplicated = Done(history, chosen, config => OverlayEdits.Duplicate(config, 0));
        var taken = styles.Rename(duplicated, history, "Night", "dawn");
        var renamed = Done(styles.Rename(duplicated, history, "Night", "Dusk"));
        var undone = new List<UndoResult> { history.Undo(renamed)! };
        undone.Add(history.Undo(undone[^1].Config)!);
        var redone = history.Redo(undone[^1].Config)!;

        Assert.Equal(Refusal.StyleNameTaken, taken.Refused);
        Assert.Equal(["Dusk", "Dusk"], renamed.Overlays.Select(overlay => overlay.Style.Name));
        Assert.Equal(["Dawn", "Dusk"], Names(styles));
        Assert.Equal(["Dusk"], undone[0].Config.Overlays.Select(overlay => overlay.Style.Name));
        Assert.Equal(File(own), File(undone[1].Config));
        Assert.All(undone, result => Assert.Equal(0, result.Skipped));
        Assert.Equal("Dusk", redone.Config.Overlays[0].Style.Name);
        Assert.Equal("DUSK", Done(styles.Rename(redone.Config, history, "dusk", "DUSK")).Overlays[0].Style.Name);
        Assert.Equal(["Dawn", "DUSK"], Names(styles));
        Assert.Equal(2, Directory.GetFiles(folder.Path).Length);
    }

    /// <summary>A folder of saved styles of the test's own, under its output, deleted after it.</summary>
    private sealed class StyleFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "styles", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
