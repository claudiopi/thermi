import { describe, expect, test } from 'vitest';
import type { Style } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { enumChoices, hexColor, isColorblindSafe, pillsShown, shownStyle } from './lookModel';

const standard = { label: { size: 13, weight: 'semibold' }, value: { size: 13.5, weight: 'bold' } };
const defaults = {
  pills: 'system',
  layout: 'pills',
  orientation: 'horizontal',
  background: 1,
  overlay: 1,
  gap: 8,
  units: true,
  font: 'segoe',
  corners: 'round',
  effect: 'auto',
  colors: {},
  ...standard,
};
const own = { label: { size: 20, weight: 'regular' }, value: { size: 22, weight: 'regular' } } as const;

describe('a look', () => {
  test.each([
    { style: undefined, shown: defaults },
    { style: { preset: 'compact' }, shown: { ...defaults, label: { size: 11, weight: 'semibold' }, value: { size: 12, weight: 'bold' } } },
    { style: { preset: 'large', ...own }, shown: { ...defaults, label: { size: 16, weight: 'semibold' }, value: { size: 17, weight: 'bold' } } },
    { style: { preset: 'custom', ...own }, shown: { ...defaults, ...own } },
  ] satisfies { style: Style | undefined; shown: unknown }[])('shows $style with every field', ({ style, shown }) => {
    expect(shownStyle(style)).toEqual(shown);
  });

  test.each([
    { colors: { normal: '#0072b2', warning: '#e69f00', critical: '#d55e00', label: '#FFFFFF' }, safe: true },
    { colors: { normal: '#0072B2', warning: '#E69F00' }, safe: false },
  ])('knows $colors colorblind-safe: $safe', ({ colors, safe }) => {
    expect(isColorblindSafe(colors)).toBe(safe);
  });

  test.each([
    ['a1b2c3', '#A1B2C3'],
    ['  #a1B2c3 ', '#A1B2C3'],
    ['#a1b2c3d', null],
    ['##a1b2c3', null],
  ])('takes "%s" in the hex box as %s', (typed, color) => {
    expect(hexColor(typed)).toBe(color);
  });

  // A sample draws its pills as the overlay does: "Match taskbar" in the taskbar's mode, never the window's.
  test.each([
    { pills: 'system', lightTaskbar: true, shown: 'light' },
    { pills: 'system', lightTaskbar: false, shown: 'dark' },
    { pills: 'light', lightTaskbar: false, shown: 'light' },
  ] as const)('draws $pills pills $shown on a taskbar light: $lightTaskbar', ({ pills, lightTaskbar, shown }) => {
    expect(pillsShown(pills, lightTaskbar)).toBe(shown);
  });

  test('words each choice by the text of its enum', () => {
    expect(enumChoices(sayKeys, 'PillTheme', ['dark', 'system'])).toEqual([
      { value: 'dark', text: 'PillTheme.Dark' },
      { value: 'system', text: 'PillTheme.System' },
    ]);
  });
});
