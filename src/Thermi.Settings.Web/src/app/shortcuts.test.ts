import { describe, expect, test } from 'vitest';
import { shortcutOf, type Press, type Shortcuts } from './shortcuts';

const names = ['undo', 'redo', 'add', 'find', 'settings', 'sheet', 'paste', 'escape'] as const;
const run = Object.fromEntries(names.map((name) => [name, () => name])) as unknown as Shortcuts;
const nameOf = (shortcut: (() => void) | null): string | null => (shortcut === null ? null : String((shortcut as () => string)()));

const press = (key: string, held: Partial<Press> = {}): Press => ({
  key,
  ctrlKey: false,
  altKey: false,
  shiftKey: false,
  metaKey: false,
  defaultPrevented: false,
  ...held,
});
const ctrl = { ctrlKey: true };

describe("the window's shortcuts", () => {
  test.each([
    { press: press('y', ctrl), typing: false, runs: 'redo' },
    { press: press('v', ctrl), typing: false, runs: 'paste' },
    { press: press('v', ctrl), typing: true, runs: null },
    { press: press('v', { ...ctrl, shiftKey: true }), typing: false, runs: null },
    { press: press('z', { ...ctrl, altKey: true }), typing: false, runs: null },
    { press: press('F1'), typing: true, runs: 'sheet' },
    { press: press('?'), typing: false, runs: 'sheet' },
    { press: press('?'), typing: true, runs: null },
    { press: press('F1', { altKey: true }), typing: false, runs: null },
    { press: press('Escape', { metaKey: true }), typing: false, runs: null },
    { press: press('Escape'), typing: true, runs: 'escape' },
    { press: press('Escape', { defaultPrevented: true }), typing: false, runs: null },
    { press: press('a'), typing: false, runs: null },
  ])('$press.key, typing $typing, runs $runs', ({ press: pressed, typing, runs }) => {
    expect(nameOf(shortcutOf(pressed, typing, run))).toBe(runs);
  });
});
