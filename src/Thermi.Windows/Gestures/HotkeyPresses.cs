using Thermi.App.SettingsFile;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;
using Thermi.Windows.Overlay;

namespace Thermi.Windows.Gestures;

/// <summary>What each of thermi's hotkeys does when pressed, anywhere in Windows.</summary>
internal static class HotkeyPresses
{
    /// <summary>
    /// The hotkey of <paramref name="id"/>, as <see cref="HotkeyRegistry"/> registered it: hiding the overlays, wearing
    /// the next saved look, turning click-through on or off, or starting the values' minimums and maximums again.
    /// </summary>
    public static void Pressed(int id, Settings settings, OverlayVisibility visibility, ItemHistory history)
    {
        switch (HotkeyRegistry.RoleOf(id))
        {
            case Core.HotkeyRole.Toggle:
                visibility.PressHotkey();
                break;
            case Core.HotkeyRole.NextStyle:
                var saved = new SavedStyles(ConfigStore.StylesFolder(settings.FilePath)).List();
                settings.Edit(config => OverlayEdits.NextStyle(config, saved));
                break;
            case Core.HotkeyRole.ClickThrough:
                settings.Edit(config => ThermiEdits.ToggleClickThrough(config).Config);
                break;
            case Core.HotkeyRole.ResetMinMax:
                history.Reset();
                break;
            default:
                break;
        }
    }
}
