import { describe, expect, test } from 'vitest';
import type { Style } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { enumChoices, hexColor, isColorblindSafe, pillSizes, shownStyle } from './lookModel';

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
    { style: { preset: 'custom' }, shown: defaults },
    {
      style: {
        pills: 'dark',
        layout: 'one',
        orientation: 'vertical',
        opacity: { background: 0.5, overlay: 0.7 },
        gap: 4,
        units: false,
        font: 'consolas',
        corners: 'square',
        effect: 'outline',
        colors: { normal: '#000000' },
      },
      shown: {
        ...defaults,
        pills: 'dark',
        layout: 'one',
        orientation: 'vertical',
        background: 0.5,
        overlay: 0.7,
        gap: 4,
        units: false,
        font: 'consolas',
        corners: 'square',
        effect: 'outline',
        colors: { normal: '#000000' },
      },
    },
  ] satisfies { style: Style | undefined; shown: unknown }[])('shows $style with every field', ({ style, shown }) => {
    expect(shownStyle(style)).toEqual(shown);
  });

  test.each([
    { colors: { normal: '#0072B2', warning: '#E69F00', critical: '#D55E00' }, safe: true },
    { colors: { normal: '#0072b2', warning: '#e69f00', critical: '#d55e00', label: '#FFFFFF' }, safe: true },
    { colors: { normal: '#0072B2', warning: '#E69F00', critical: '#F05A50' }, safe: false },
    { colors: { normal: '#0072B2', warning: '#E69F00' }, safe: false },
    { colors: {}, safe: false },
  ])('knows $colors colorblind-safe: $safe', ({ colors, safe }) => {
    expect(isColorblindSafe(colors)).toBe(safe);
  });

  test.each([
    ['#a1b2c3', '#A1B2C3'],
    ['a1b2c3', '#A1B2C3'],
    ['  #a1B2c3 ', '#A1B2C3'],
    ['#a1b2c', null],
    ['#a1b2c3d', null],
    ['#g1b2c3', null],
    ['##a1b2c3', null],
    ['', null],
  ])('takes "%s" in the hex box as %s', (typed, color) => {
    expect(hexColor(typed)).toBe(color);
  });

  test.each([
    { size: 13.5, corners: 'round', sizes: { height: 31, radius: 15.5, padding: 10, gap: 6, segment: 2 } },
    { size: 13.5, corners: 'soft', sizes: { height: 31, radius: 7.75, padding: 10, gap: 6, segment: 2 } },
    { size: 13.5, corners: 'square', sizes: { height: 31, radius: 2, padding: 10, gap: 6, segment: 2 } },
    { size: 17, corners: 'round', sizes: { height: 37, radius: 18.5, padding: 12, gap: 8, segment: 3 } },
  ] as const)('sizes a sample pill of $size px text, $corners', ({ size, corners, sizes }) => {
    const style = { ...shownStyle({ corners }), value: { size, weight: 'bold' } as const };

    expect(pillSizes(style)).toEqual(sizes);
  });

  test('words each choice by the text of its enum', () => {
    expect(enumChoices(sayKeys, 'PillTheme', ['dark', 'system'])).toEqual([
      { value: 'dark', text: 'PillTheme.Dark' },
      { value: 'system', text: 'PillTheme.System' },
    ]);
  });
});
