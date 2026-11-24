import type { Item, Level, Overlay, Settings, WindowView } from '../bridge/bridge.g';
import type { TextKey } from '../texts/Texts';

// The list as rows: each overlay, each of its pills under it, each item under its pill, in the order the overlay
// draws them. A row's key names what it shows, so selection and folding survive a refresh.

export type RowKind = 'overlay' | 'pill' | 'item';

export interface Row {
  key: string;
  kind: RowKind;
  level: 1 | 2 | 3;
  /** What the row shows. */
  name: string;
  /** What Narrator reads as the row's name. */
  label: string;
  overlay: number;
  parent: string | null;
  /** Whether it has rows under it, and whether they show. */
  branch: boolean;
  expanded: boolean;
  /** An item's id, or a pill's items. */
  items: string[];
  /** An item's value now, or "Hidden" on a hidden overlay's row. */
  value?: string | null;
  alert?: Level | null;
}

/** The key of the Settings row, below the list. */
export const settingsKey = 'settings';

export const overlayKey = (index: number): string => `o:${index}`;
export const itemKey = (id: string): string => `i:${id}`;
export const pillKey = (overlay: number, first: string): string => `p:${overlay}:${first}`;

type Say = (key: TextKey, ...blanks: (string | number)[]) => string;

export function overlayName(overlay: Overlay, index: number, say: Say): string {
  return overlay.name ?? say('UiText.OverlayName', index + 1);
}

export function itemName(item: Item): string {
  return item.name ?? item.id;
}

/** Every row, folded or not, in order. */
export function rowsOf(settings: Settings, view: WindowView, folded: ReadonlySet<string>, say: Say): Row[] {
  const rows: Row[] = [];
  settings.overlays.forEach((overlay, index) => {
    const key = overlayKey(index);
    const name = overlayName(overlay, index, say);
    const count = overlay.items.length;
    const label = count === 1 ? say('UiText.OverlayOneItem', name) : say('UiText.OverlayItems', name, count);
    // A hidden overlay shows only here, so its row says it where an item's says its value.
    const hidden = overlay.hidden === true ? { value: say('UiText.OverlayHidden') } : {};
    rows.push({
      key,
      kind: 'overlay',
      level: 1,
      name,
      label,
      overlay: index,
      parent: null,
      branch: true,
      expanded: !folded.has(key),
      items: overlay.items.map((item) => item.id),
      ...hidden,
    });
    const byId = new Map(overlay.items.map((item) => [item.id, item]));
    for (const pill of view.pills[index] ?? []) {
      const first = pill.items[0] ?? '';
      const pkey = pillKey(index, first);
      const pillLabel = pill.items.length === 1 ? say('UiText.PillOneItem', pill.label) : say('UiText.PillItems', pill.label, pill.items.length);
      rows.push({
        key: pkey,
        kind: 'pill',
        level: 2,
        name: pill.label,
        label: pillLabel,
        overlay: index,
        parent: key,
        branch: true,
        expanded: !folded.has(pkey),
        items: pill.items,
      });
      for (const id of pill.items) {
        const item = byId.get(id);
        const shows = view.items[id];
        const name = item === undefined ? id : itemName(item);
        rows.push({
          key: itemKey(id),
          kind: 'item',
          level: 3,
          name,
          label: name,
          overlay: index,
          parent: pkey,
          branch: false,
          expanded: false,
          items: [id],
          value: shows?.value ?? null,
          alert: shows?.level ?? null,
        });
      }
    }
  });
  return rows;
}

/** The rows that show: those whose overlay and pill are open. */
export function shownRows(rows: readonly Row[]): Row[] {
  const shown: Row[] = [];
  let overlayOpen = true;
  let pillOpen = true;
  for (const row of rows) {
    if (row.level === 1) {
      overlayOpen = row.expanded;
    } else if (row.level === 2) {
      pillOpen = row.expanded;
    }
    if (row.level === 1 || (overlayOpen && (row.level === 2 || pillOpen))) {
      shown.push(row);
    }
  }
  return shown;
}

/** The next row after `from` whose name starts with the typed text, going round, or null. */
export function rowStarting(rows: readonly Row[], from: number, typed: string): Row | null {
  const wanted = typed.toLowerCase();
  // A letter typed again moves on to the next row it starts, so the search starts after the current row.
  const start = wanted.length === 1 ? from + 1 : from;
  for (let step = 0; step < rows.length; step += 1) {
    const row = rows[(start + step) % rows.length];
    if (row !== undefined && row.name.toLowerCase().startsWith(wanted)) {
      return row;
    }
  }
  return null;
}

/** Where an item lands when Alt+Up or Alt+Down moves it within its overlay, or null at an end. */
export function movedIndex(settings: Settings, id: string, by: -1 | 1): { overlay: number; index: number } | null {
  for (const [overlay, one] of settings.overlays.entries()) {
    const index = one.items.findIndex((item) => item.id === id);
    if (index >= 0) {
      const next = index + by;
      return next < 0 || next >= one.items.length ? null : { overlay, index: next };
    }
  }
  return null;
}

/** The item to select once one goes: the next in its overlay, else the one before, else the overlay. */
export function afterRemoving(settings: Settings, id: string): string {
  for (const [overlay, one] of settings.overlays.entries()) {
    const index = one.items.findIndex((item) => item.id === id);
    if (index >= 0) {
      const next = one.items[index + 1] ?? one.items[index - 1];
      return next === undefined ? overlayKey(overlay) : itemKey(next.id);
    }
  }
  return settingsKey;
}
