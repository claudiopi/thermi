using Thermi.App.SettingsFile;
using Thermi.Core;
using Thermi.Core.Editing;
using Thermi.Core.Look;
using Thermi.Core.Placement;

namespace Thermi.App.Gestures;

/// <summary>What each of thermi's hotkeys does when pressed, anywhere on the desktop, on every OS.</summary>
internal static class HotkeyPresses
{
    /// <summary>
    /// The hotkey of <paramref name="role"/>: hiding the overlays, wearing the next saved look, turning click-through on or
    /// off, or starting the values' minimums and maximums again.
    /// </summary>
    public static void Pressed(HotkeyRole role, Settings settings, OverlayVisibility visibility, ItemHistory history)
    {
        switch (role)
        {
            case HotkeyRole.Toggle:
                visibility.PressHotkey();
                break;
            case HotkeyRole.NextStyle:
                var saved = new SavedStyles(ConfigStore.StylesFolder(settings.FilePath)).List();
                settings.Edit(config => OverlayEdits.NextStyle(config, saved));
                break;
            case HotkeyRole.ClickThrough:
                settings.Edit(config => ThermiEdits.ToggleClickThrough(config).Config);
                break;
            case HotkeyRole.ResetMinMax:
                history.Reset();
                break;
            default:
                break;
        }
    }
}
