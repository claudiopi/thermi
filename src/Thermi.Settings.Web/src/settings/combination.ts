import type { KeyboardEvent } from 'react';

// A hotkey as the settings file writes it, like "Ctrl+Alt+T": the modifiers in Windows' order, then the key, by its
// place on the keyboard, so a layout that types another character there still names the same key. Whether it may be
// a hotkey is Core's; the page only names what was pressed.

const modifierKeys = new Set(['Control', 'Alt', 'Shift', 'Meta', 'AltGraph', 'OS']);

const named: Record<string, string> = {
  Space: 'Space',
  PageUp: 'PageUp',
  PageDown: 'PageDown',
  End: 'End',
  Home: 'Home',
  ArrowLeft: 'Left',
  ArrowUp: 'Up',
  ArrowRight: 'Right',
  ArrowDown: 'Down',
  Insert: 'Insert',
  Delete: 'Delete',
  Escape: 'Esc',
  Tab: 'Tab',
};

/** The key's name, or null for one no hotkey can take, like a modifier alone. */
function keyOf(code: string): string | null {
  const letter = /^Key([A-Z])$/.exec(code) ?? /^Digit(\d)$/.exec(code) ?? /^(F\d{1,2})$/.exec(code);
  return letter?.[1] ?? named[code] ?? null;
}

/** What a key press means while a hotkey records: a combination, Esc's cancel, Backspace's clear, or nothing yet. */
export type Pressed = { kind: 'combination'; combination: string } | { kind: 'cancel' } | { kind: 'clear' } | { kind: 'away' } | null;

export function pressed(event: KeyboardEvent): Pressed {
  if (modifierKeys.has(event.key)) {
    return null;
  }
  const plain = !event.ctrlKey && !event.altKey && !event.metaKey && !event.shiftKey;
  if (plain && event.key === 'Escape') {
    return { kind: 'cancel' };
  }
  if (plain && event.key === 'Backspace') {
    return { kind: 'clear' };
  }
  // Tab moves on, as everywhere: a box that kept it would trap the keyboard.
  if (event.key === 'Tab' && !event.ctrlKey && !event.altKey && !event.metaKey) {
    return { kind: 'away' };
  }
  const key = keyOf(event.code);
  if (key === null) {
    return null;
  }
  const modifiers = [event.ctrlKey && 'Ctrl', event.altKey && 'Alt', event.shiftKey && 'Shift', event.metaKey && 'Win'].filter((one) => one !== false);
  return { kind: 'combination', combination: [...modifiers, key].join('+') };
}
