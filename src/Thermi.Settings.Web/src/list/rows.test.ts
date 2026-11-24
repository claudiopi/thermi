import { describe, expect, test } from 'vitest';
import type { Item, Overlay, Settings, WindowView } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { afterRemoving, movedIndex, rowStarting, rowsOf, shownRows, type Row } from './rows';

function overlay(name: string, ...items: Item[]): Overlay {
  return { name, items };
}

function settings(...overlays: Overlay[]): Settings {
  return { version: 1, overlays };
}

const ids = (...names: string[]): Item[] => names.map((id) => ({ id }));

// A row as one line: what it is, what Narrator reads, where it hangs and what it shows.
const line = (row: Row): string =>
  `${row.key} ${row.kind} ${row.level} ${row.name} | ${row.label} | ${row.parent ?? '-'} ${row.branch} ${row.expanded} [${row.items.join(',')}] ${row.value ?? '-'} ${row.alert ?? '-'}`;

function row(key: string, level: 1 | 2 | 3, expanded = true): Row {
  return { key, kind: level === 1 ? 'overlay' : level === 2 ? 'pill' : 'item', level, name: key, label: key, overlay: 0, parent: null, branch: level < 3, expanded, items: [] };
}

describe('the list', () => {
  test('rows every overlay, its pills and their items, in the order the overlay draws them', () => {
    const shown = settings(overlay('Main', { id: 'cpu' }, { id: 'gpu', name: 'Graphics' }), { ...overlay('Two', { id: 'ram' }), name: undefined as unknown as string, hidden: true });
    const view: WindowView = {
      pills: [[{ label: 'CPU', items: ['cpu'] }, { label: 'Pair', items: ['gpu', 'ghost'] }]],
      items: { cpu: { value: '50 °C', level: 'warning', type: 'Temperature', meters: [], meterName: null } },
      values: {},
      mark: 'calm',
    };

    const rows = rowsOf(shown, view, new Set(['p:0:gpu']), sayKeys);

    expect(rows.map(line)).toEqual([
      'o:0 overlay 1 Main | UiText.OverlayItems Main 2 | - true true [cpu,gpu] - -',
      'p:0:cpu pill 2 CPU | UiText.PillOneItem CPU | o:0 true true [cpu] - -',
      'i:cpu item 3 cpu | cpu | p:0:cpu false false [cpu] 50 °C warning',
      'p:0:gpu pill 2 Pair | UiText.PillItems Pair 2 | o:0 true false [gpu,ghost] - -',
      'i:gpu item 3 Graphics | Graphics | p:0:gpu false false [gpu] - -',
      'i:ghost item 3 ghost | ghost | p:0:gpu false false [ghost] - -',
      'o:1 overlay 1 UiText.OverlayName 2 | UiText.OverlayOneItem UiText.OverlayName 2 | - true true [ram] UiText.OverlayHidden -',
    ]);
  });

  test('shows only the rows whose overlay and pill are open', () => {
    const rows = [row('o0', 1), row('p0a', 2, false), row('i1', 3), row('p0b', 2), row('i2', 3), row('o1', 1, false), row('p1', 2), row('i3', 3), row('o2', 1), row('p2', 2), row('i4', 3)];

    expect(shownRows(rows).map((one) => one.key)).toEqual(['o0', 'p0a', 'p0b', 'i2', 'o1', 'o2', 'p2', 'i4']);
  });

  const named = ['Main', 'cpu', 'Graphics', 'gpu', 'Two'].map((name) => ({ ...row(name, 3), name }));
  test.each([
    { from: 0, typed: 'g', found: 'Graphics' },
    { from: 2, typed: 'g', found: 'gpu' },
    { from: 3, typed: 'g', found: 'Graphics' },
    { from: 2, typed: 'gr', found: 'Graphics' },
    { from: 0, typed: 'GP', found: 'gpu' },
    { from: 4, typed: 't', found: 'Two' },
    { from: 0, typed: 'x', found: null },
  ])('finds the row that "$typed" starts after row $from', ({ from, typed, found }) => {
    expect(rowStarting(named, from, typed)?.name ?? null).toBe(found);
  });

  const three = settings(overlay('A', ...ids('a', 'b', 'c')), overlay('B', ...ids('d', 'e')), overlay('C', ...ids('f')));
  test.each([
    { id: 'b', by: -1, to: { overlay: 0, index: 0 } },
    { id: 'b', by: 1, to: { overlay: 0, index: 2 } },
    { id: 'e', by: -1, to: { overlay: 1, index: 0 } },
    { id: 'a', by: -1, to: null },
    { id: 'c', by: 1, to: null },
    { id: 'f', by: 1, to: null },
    { id: 'gone', by: 1, to: null },
  ] as const)('moves $id by $by', ({ id, by, to }) => {
    expect(movedIndex(three, id, by)).toEqual(to);
  });

  test.each([
    { id: 'a', selected: 'i:b' },
    { id: 'c', selected: 'i:b' },
    { id: 'e', selected: 'i:d' },
    { id: 'f', selected: 'o:2' },
    { id: 'gone', selected: 'settings' },
  ])('selects $selected once $id goes', ({ id, selected }) => {
    expect(afterRemoving(three, id)).toBe(selected);
  });
});
