import { describe, expect, test } from 'vitest';
import type { Item, Overlay, Settings, WindowView } from '../bridge/bridge.g';
import { sayKeys } from '../testSay';
import { afterRemoving, rowStarting, rowsOf, shownRows, type Row } from './rows';

function overlay(name: string, ...items: Item[]): Overlay {
  return { name, items };
}

function settings(...overlays: Overlay[]): Settings {
  return { version: 1, overlays };
}

const ids = (...names: string[]): Item[] => names.map((id) => ({ id }));

// What the list decides of a row: what it is, which text Narrator reads it by and with what, where it hangs and what it
// shows. The texts are their keys and blanks, as sayKeys gives them, never their words.
const decided = (row: Row) => ({
  key: row.key,
  kind: row.kind,
  name: row.name,
  said: row.label,
  parent: row.parent,
  branch: row.branch,
  expanded: row.expanded,
  items: row.items,
  value: row.value ?? null,
  alert: row.alert ?? null,
});

function row(key: string, level: 1 | 2 | 3, expanded = true): Row {
  return {
    key,
    kind: level === 1 ? 'overlay' : level === 2 ? 'pill' : 'item',
    level,
    name: key,
    label: key,
    overlay: 0,
    parent: null,
    branch: level < 3,
    expanded,
    items: [],
  };
}

describe('the list', () => {
  test('rows every overlay, its pills and their items, in the order the overlay draws them', () => {
    const shown = settings(overlay('Main', { id: 'cpu' }, { id: 'gpu', name: 'Graphics' }), {
      ...overlay('Two', { id: 'ram' }),
      name: undefined as unknown as string,
      hidden: true,
    });
    const view: WindowView = {
      pills: [[{ label: 'CPU', items: ['cpu'] }, { label: 'Pair', items: ['gpu', 'ghost'] }]],
      items: {
        cpu: { value: '50 °C', level: 'warning', type: 'Temperature', warnBy: [], warnByName: null, judged: true, bits: false, offer: { first: [], more: [] } },
      },
      values: {},
      mark: 'calm',
      colors: [],
      fitted: [],
    };

    const rows = rowsOf(shown, view, new Set(['p:0:gpu']), sayKeys);

    expect(rows.map(decided)).toEqual([
      {
        key: 'o:0',
        kind: 'overlay',
        name: 'Main',
        said: sayKeys('UiText.OverlayItems', 'Main', 2),
        parent: null,
        branch: true,
        expanded: true,
        items: ['cpu', 'gpu'],
        value: null,
        alert: null,
      },
      {
        key: 'p:0:cpu',
        kind: 'pill',
        name: 'CPU',
        said: sayKeys('UiText.PillOneItem', 'CPU'),
        parent: 'o:0',
        branch: true,
        expanded: true,
        items: ['cpu'],
        value: null,
        alert: null,
      },
      {
        key: 'i:cpu',
        kind: 'item',
        name: 'cpu',
        said: 'cpu',
        parent: 'p:0:cpu',
        branch: false,
        expanded: false,
        items: ['cpu'],
        value: '50 °C',
        alert: 'warning',
      },
      {
        key: 'p:0:gpu',
        kind: 'pill',
        name: 'Pair',
        said: sayKeys('UiText.PillItems', 'Pair', 2),
        parent: 'o:0',
        branch: true,
        expanded: false,
        items: ['gpu', 'ghost'],
        value: null,
        alert: null,
      },
      {
        key: 'i:gpu',
        kind: 'item',
        name: 'Graphics',
        said: 'Graphics',
        parent: 'p:0:gpu',
        branch: false,
        expanded: false,
        items: ['gpu'],
        value: null,
        alert: null,
      },
      {
        key: 'i:ghost',
        kind: 'item',
        name: 'ghost',
        said: 'ghost',
        parent: 'p:0:gpu',
        branch: false,
        expanded: false,
        items: ['ghost'],
        value: null,
        alert: null,
      },
      {
        key: 'o:1',
        kind: 'overlay',
        name: sayKeys('UiText.OverlayName', 2),
        said: sayKeys('UiText.OverlayOneItem', sayKeys('UiText.OverlayName', 2)),
        parent: null,
        branch: true,
        expanded: true,
        items: ['ram'],
        value: sayKeys('UiText.OverlayHidden'),
        alert: null,
      },
    ]);
  });

  test('shows only the rows whose overlay and pill are open', () => {
    const rows = [
      row('o0', 1),
      row('p0a', 2, false),
      row('i1', 3),
      row('p0b', 2),
      row('i2', 3),
      row('o1', 1, false),
      row('p1', 2),
      row('i3', 3),
      row('o2', 1),
      row('p2', 2),
      row('i4', 3),
    ];

    expect(shownRows(rows).map((one) => one.key)).toEqual(['o0', 'p0a', 'p0b', 'i2', 'o1', 'o2', 'p2', 'i4']);
  });

  const named = ['Main', 'cpu', 'Graphics', 'gpu', 'Two'].map((name) => ({ ...row(name, 3), name }));
  test.each([
    { from: 2, typed: 'g', found: 'gpu' },
    { from: 3, typed: 'g', found: 'Graphics' },
  ])('finds the row that "$typed" starts after row $from', ({ from, typed, found }) => {
    expect(rowStarting(named, from, typed)?.name ?? null).toBe(found);
  });

  const three = settings(overlay('A', ...ids('a', 'b', 'c')), overlay('B', ...ids('d', 'e')), overlay('C', ...ids('f')));

  // Rows: the next item, the one before, the overlay, and settings once nothing is left, each a line of its own.
  test.each([
    { id: 'a', selected: 'i:b' },
    { id: 'e', selected: 'i:d' },
    { id: 'f', selected: 'o:2' },
    { id: 'gone', selected: 'settings' },
  ])('selects $selected once $id goes', ({ id, selected }) => {
    expect(afterRemoving(three, id)).toBe(selected);
  });
});
