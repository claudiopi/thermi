import type { KeyboardEvent } from 'react';
import { expect, test } from 'vitest';
import { pressed, type Pressed } from './combination';

type Keys = { ctrl?: boolean; alt?: boolean; shift?: boolean; win?: boolean };

function press(key: string, code: string, { ctrl = false, alt = false, shift = false, win = false }: Keys = {}): KeyboardEvent {
  return { key, code, ctrlKey: ctrl, altKey: alt, shiftKey: shift, metaKey: win } as KeyboardEvent;
}

const combination = (text: string): Pressed => ({ kind: 'combination', combination: text });

test.each([
  { key: 'Control', code: 'KeyA', keys: { ctrl: true }, means: null },
  { key: 'AltGraph', code: 'KeyA', keys: {}, means: null },
  { key: 'Alt', code: 'KeyA', keys: { alt: true }, means: null },
  { key: 'Shift', code: 'KeyA', keys: { shift: true }, means: null },
  { key: 'Meta', code: 'KeyA', keys: { win: true }, means: null },
  { key: 'OS', code: 'KeyA', keys: { win: true }, means: null },
  { key: 'PageUp', code: 'PageUp', keys: { ctrl: true }, means: combination('Ctrl+PageUp') },
  { key: 'PageDown', code: 'PageDown', keys: { ctrl: true }, means: combination('Ctrl+PageDown') },
  { key: 'End', code: 'End', keys: { ctrl: true }, means: combination('Ctrl+End') },
  { key: 'Home', code: 'Home', keys: { ctrl: true }, means: combination('Ctrl+Home') },
  { key: 'ArrowUp', code: 'ArrowUp', keys: { ctrl: true }, means: combination('Ctrl+Up') },
  { key: 'ArrowRight', code: 'ArrowRight', keys: { ctrl: true }, means: combination('Ctrl+Right') },
  { key: 'ArrowDown', code: 'ArrowDown', keys: { ctrl: true }, means: combination('Ctrl+Down') },
  { key: 'Insert', code: 'Insert', keys: { ctrl: true }, means: combination('Ctrl+Insert') },
  { key: 'Delete', code: 'Delete', keys: { ctrl: true }, means: combination('Ctrl+Delete') },
  { key: '0', code: 'Digit0', keys: { ctrl: true }, means: combination('Ctrl+0') },
  { key: 'a', code: 'xKeyA', keys: { ctrl: true }, means: null },
  { key: '1', code: 'Digit12', keys: { ctrl: true }, means: null },
  { key: '1', code: 'xDigit1', keys: { ctrl: true }, means: null },
  { key: 'F1', code: 'xF1', keys: {}, means: null },
  { key: 'Escape', code: 'Escape', keys: {}, means: { kind: 'cancel' } },
  { key: 'Escape', code: 'Escape', keys: { ctrl: true }, means: combination('Ctrl+Esc') },
  { key: 'Escape', code: 'Escape', keys: { alt: true }, means: combination('Alt+Esc') },
  { key: 'Escape', code: 'Escape', keys: { shift: true }, means: combination('Shift+Esc') },
  { key: 'Escape', code: 'Escape', keys: { win: true }, means: combination('Win+Esc') },
  { key: 'Backspace', code: 'Backspace', keys: {}, means: { kind: 'clear' } },
  { key: 'Tab', code: 'Tab', keys: {}, means: { kind: 'away' } },
  { key: 'Tab', code: 'Tab', keys: { shift: true }, means: { kind: 'away' } },
  { key: 'Tab', code: 'Tab', keys: { ctrl: true }, means: combination('Ctrl+Tab') },
  { key: 'Tab', code: 'Tab', keys: { alt: true }, means: combination('Alt+Tab') },
  { key: 'Tab', code: 'Tab', keys: { win: true }, means: combination('Win+Tab') },
  { key: 'à', code: 'KeyQ', keys: { ctrl: true, shift: true }, means: combination('Ctrl+Shift+Q') },
  { key: 'F12', code: 'F12', keys: {}, means: combination('F12') },
  { key: 'ArrowLeft', code: 'ArrowLeft', keys: { ctrl: true, alt: true, shift: true, win: true }, means: combination('Ctrl+Alt+Shift+Win+Left') },
  { key: ' ', code: 'Space', keys: { ctrl: true }, means: combination('Ctrl+Space') },
  { key: 'a', code: 'KeyAB', keys: { ctrl: true }, means: null },
  { key: 'F1', code: 'F123', keys: {}, means: null },
] satisfies {
  key: string;
  code: string;
  keys: Keys;
  means: Pressed;
}[])('a hotkey box reads $key on $code with $keys as $means', ({ key, code, keys, means }) => {
  expect(pressed(press(key, code, keys))).toEqual(means);
});
