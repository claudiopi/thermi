using Thermi.Core.Editing;
using Thermi.Core.Settings;
using Thermi.Core.Tray;

namespace Thermi.Tests;

/// <summary>Where a click on a tray icon leads.</summary>
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
}
