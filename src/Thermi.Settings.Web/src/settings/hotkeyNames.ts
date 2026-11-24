import type { HotkeyRole } from '../bridge/bridge.g';
import type { TextKey } from '../texts/Texts';

/** Each hotkey's name, as its row and the shortcut sheet say it. */
export const hotkeyNames: Record<HotkeyRole, TextKey> = {
  toggle: 'WindowText.HotkeyToggle',
  nextStyle: 'WindowText.HotkeyNextLook',
  clickThrough: 'WindowText.HotkeyClickThrough',
  resetMinMax: 'WindowText.HotkeyResetMinMax',
};
