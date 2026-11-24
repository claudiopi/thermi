import type { Monitor, Overlay, Snap, SnapSpot, Value } from '../bridge/bridge.g';
import type { TextKey } from '../texts/Texts';

// Where an overlay sits, as the Placement column and the page's subtitle say it: its spot, its display counted from
// the main one, and a display that is gone shown as the main one, as the overlay itself does.

type Say = (key: TextKey, ...blanks: (string | number)[]) => string;

export const taskbarSpots: readonly SnapSpot[] = ['taskbar-left', 'taskbar-center', 'taskbar-right'];
export const cornerSpots: readonly SnapSpot[] = ['top-left', 'top-right', 'bottom-left', 'bottom-right'];

/** A snap spot's text key: `taskbar-left` is `SnapSpot.TaskbarLeft`. */
export function spotKey(spot: SnapSpot): TextKey {
  return `SnapSpot.${spot.replace(/(^|-)(\w)/g, (_, _dash: string, letter: string) => letter.toUpperCase())}`;
}

/** The displays as the window counts them: the main one first, the others in the order Windows gives them. */
export function displays(monitors: readonly Monitor[]): Monitor[] {
  return [...monitors.filter((one) => one.primary), ...monitors.filter((one) => !one.primary)];
}

export interface Placed {
  spot: SnapSpot;
  /** The display's place in `displays`, from 0. */
  display: number;
  /** Its display is not connected, so it shows on the main one. */
  gone: boolean;
}

/** The overlay's snap as it shows now, or null for one left where it was dropped. */
export function placedOf(overlay: Overlay, shown: readonly Monitor[]): Placed | null {
  if (overlay.snap === undefined && overlay.position !== undefined) {
    return null;
  }
  const snap: Snap = overlay.snap ?? { spot: 'taskbar-right' };
  const at = snap.monitor === undefined ? 0 : shown.findIndex((one) => one.id === snap.monitor);
  return { spot: snap.spot, display: Math.max(0, at), gone: at < 0 };
}

/** The snap a spot of the map writes, as an overlay edit's value: the main display is left out, as the file does. */
export function snapAt(spot: SnapSpot, display: Monitor | undefined): { [field: string]: Value } {
  return display === undefined || display.primary ? { spot } : { spot, monitor: display.id };
}

/** "Move to display": the same spot on another display, a free overlay going to its top right. */
export function moveToDisplay(overlay: Overlay, display: Monitor): { [field: string]: Value } {
  return snapAt(overlay.snap?.spot ?? (overlay.position === undefined ? 'taskbar-right' : 'top-right'), display);
}

const capital = (text: string): string => text.charAt(0).toLocaleUpperCase() + text.slice(1);

/** Where the overlay sits, as "Right of the taskbar" or "Top right corner, display 2". */
export function placeText(overlay: Overlay, shown: readonly Monitor[], say: Say): string {
  const placed = placedOf(overlay, shown);
  if (placed === null) {
    return say('NameText.Floating');
  }
  const spot = capital(say(spotKey(placed.spot)));
  return placed.display === 0 ? spot : say('OverlayPageText.SpotOnMonitor', spot, placed.display + 1);
}

/** A display's name on the map and in "Move to display". */
export function displayName(display: Monitor, index: number, say: Say): string {
  return display.primary ? say('OverlayPageText.MainDisplay', index + 1) : say('OverlayPageText.DisplayName', index + 1);
}
