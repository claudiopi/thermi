import { describe, expect, test } from 'vitest';
import type { Item, ItemView, Settings } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { shownBy } from './shownBy';

const cpu: Item = { id: 'cpu.temperature', name: 'CPU temperature', metric: 'cpu.temperature' };
const views: Record<string, ItemView> = { 'cpu.temperature': { auto: '/amdcpu/0/temperature/2' } as ItemView };

// QA r4 (R4-10): two overlays showing the same sensor say which overlay each item is on; one overlay needs no saying.
describe('the items a catalog row says show its sensor', () => {
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
