using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Thermi.App.Gestures;
using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Placement;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Tests;

/// <summary>Where a click on a tray icon, or on a button of one of thermi's notices, leads, on every OS.</summary>
public sealed class TrayClickTests
{
    [Fact]
    public void AnIconOpensTheWindowOnItsItemsOverlayOrTheFirst()
    {
        var two = OverlayEdits.Add(new ThermiConfig()).Config;
        var second = two.Overlays[1].Items[2].Id;

        Assert.Equal(1, TrayScene.OverlayOf(two, second));
        Assert.Equal(0, TrayScene.OverlayOf(two, two.Overlays[0].Items[0].Id));
        Assert.Equal(0, TrayScene.OverlayOf(two, null));
        Assert.Equal(0, TrayScene.OverlayOf(two, "gone"));
    }

    // A click on thermi's icon shows the overlays it hid, or opens its menu; an item's opens its menu, and its double-click
    // the window on it; a double-click on thermi's does nothing more than its two clicks.
    [Theory]
    [InlineData(null, nameof(TrayPress.Activate), true, nameof(TrayAnswer.Paint))]
    [InlineData(null, nameof(TrayPress.Activate), false, nameof(TrayAnswer.Menu))]
    [InlineData(null, nameof(TrayPress.Menu), true, nameof(TrayAnswer.Menu))]
    [InlineData(null, nameof(TrayPress.Twice), false, nameof(TrayAnswer.None))]
    [InlineData("cpu.temperature", nameof(TrayPress.Activate), false, nameof(TrayAnswer.None))]
    [InlineData("cpu.temperature", nameof(TrayPress.Menu), false, nameof(TrayAnswer.Menu))]
    [InlineData("cpu.temperature", nameof(TrayPress.Twice), false, nameof(TrayAnswer.Settings))]
    public void APressOnAnIconDoesWhatItDoesOnWindows(string? item, string press, bool hidden, string answer)
    {
        var visibility = new OverlayVisibility { HideOverlays = hidden };

        Assert.Equal(answer, TrayPresses.Of(item, Enum.Parse<TrayPress>(press), visibility).ToString());
        Assert.Equal(hidden && answer != nameof(TrayAnswer.Paint), visibility.HideOverlays);
    }

    // Each button does what its notice is about while that notice still shows; one whose matter ended, as another notice
    // took its place, does nothing, but the first run's, which opens the window all the same.
    [Theory]
    [InlineData(NoticeKind.OverlaysHidden, NoticeButtons.ShowThem, true, "shown")]
    [InlineData(NoticeKind.OverlaysHidden, NoticeButtons.ShowIt, true, "shown")]
    [InlineData(NoticeKind.OverlayHidden, NoticeButtons.ShowIt, true, "unhidden")]
    [InlineData(NoticeKind.ClickThrough, NoticeButtons.Undo, true, "clicks")]
    [InlineData(NoticeKind.OverlaysHidden, NoticeButtons.GotIt, true, "")]
    [InlineData(NoticeKind.OverlaysHidden, NoticeButtons.ShowThem, false, "")]
    [InlineData(NoticeKind.ClickThrough, NoticeButtons.Undo, false, "")]
    [InlineData(NoticeKind.FirstRun, NoticeButtons.None, false, "window")]
    public async Task ANoticesButtonDoesWhatItSaysWhileItsNoticeShows(NoticeKind kind, NoticeButtons button, bool showing, string done)
    {
        var folder = Directory.CreateTempSubdirectory("thermi-notice-");
        try
        {
            var (did, changed) = await ClickAsync(Path.Combine(folder.FullName, "settings.json"), new Notice(kind, button) { Overlays = [0] }, showing);

            Assert.Equal(done, did);
            Assert.Equal(done.Length > 0, changed);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // Clicks the notice's button over one overlay hidden with click-through on, and Hide overlays set: what it undid.
    private static async Task<(string Did, bool Changed)> ClickAsync(string file, Notice notice, bool showing)
    {
        // A whole file, which a save writes over at once, never waiting on the clock for one still being written.
        await File.WriteAllTextAsync(file, ThermiConfigJson.Write(new ThermiConfig()), TestContext.Current.CancellationToken);
        var time = new FakeTimeProvider();
        using var store = new ConfigStore(file, time, () => false);
        await using var settings = new Settings(store, time, NullLogger.Instance);
        settings.Edit(config => config with { Overlays = [config.Overlays[0] with { Hidden = true, ClickThrough = true }] });
        var visibility = new OverlayVisibility { HideOverlays = true };
        var opened = false;

        var changed = NoticeChoices.Choose(
            notice, notice.Buttons, showing ? notice : null, settings, visibility, (_, after) => new EditResult(after), () => opened = true);

        string[] did =
        [
            !visibility.HideOverlays ? "shown" : "",
            !settings.Config.Overlays[0].Hidden ? "unhidden" : "",
            !settings.Config.Overlays[0].ClickThrough ? "clicks" : "",
            opened ? "window" : "",
        ];
        return (string.Concat(did), changed);
    }
}
