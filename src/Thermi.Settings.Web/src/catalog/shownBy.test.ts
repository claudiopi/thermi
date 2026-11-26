import { describe, expect, test } from 'vitest';
import type { Item, ItemView, Settings } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { rowLine, shownBy } from './shownBy';

const cpu: Item = { id: 'cpu.temperature', name: 'CPU temperature', metric: 'cpu.temperature' };
const views: Record<string, ItemView> = { 'cpu.temperature': { auto: '/amdcpu/0/temperature/2' } as ItemView };

// QA r4 (R4-10): two overlays showing the same sensor say which overlay each item is on; one overlay needs no saying.
describe('the items a catalog row says show its sensor', () => {
  // Rows: one overlay names the item alone and several name its overlay too, the two sides of several.
  test.each([
    [[{ name: 'Taskbar', items: [cpu] }], ['CPU temperature']],
    [
      [
        { name: 'Taskbar', items: [cpu] },
        { name: 'Top right', items: [cpu] },
      ],
      ['CatalogText.ShownOn CPU temperature Taskbar', 'CatalogText.ShownOn CPU temperature Top right'],
    ],
  ])('%#', (overlays, said) => {
    const settings = { version: 1, overlays } as unknown as Settings;

    expect(shownBy(settings, views, '/amdcpu/0/temperature/2', sayKeys)).toEqual(said);
  });
});

// Night QA (N7): a GPU's fan at rest that an item shows said only that it rests. A fault says itself first; a quiet note,
// as a GPU's fan at rest, gives way to the items that show it, and a likely name keeps its "Likely" line (R178).
describe('the line under a catalog row', () => {
  // Rows: each is an operand of what the line says first.
  test.each([
    [{ why: 'reads-nothing', from: 'library', alias: null }, true, 'why'],
    [{ why: 'resting', from: 'library', alias: null }, true, 'shown'],
    [{ why: 'resting', from: 'library', alias: null }, false, 'why'],
    [{ why: null, from: 'library', alias: 'CPU fan' }, true, 'alias'],
    [{ why: null, from: 'likely', alias: null }, true, 'note'],
    [{ why: null, from: 'library', alias: null }, true, 'shown'],
    [{ why: null, from: 'library', alias: null }, false, 'note'],
  ] as const)('%o shown %s', (row, shown, line) => {
    expect(rowLine(row, shown)).toBe(line);
  });
});
