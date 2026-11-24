import { useLayoutEffect, useRef, useState, type DragEvent, type KeyboardEvent } from 'react';
import type { Edit } from '../bridge/bridge.g';
import { asOneStep, useEdit, type WindowData } from '../bridge/useWindow';
import { Button, Icon, Menu } from '../kit';
import { displays } from '../overlay/placeModel';
import { useMonitors } from '../overlay/useOverlayReads';
import { useText } from '../texts/Texts';
import { ListRow } from './ListRow';
import { rowKey, type ListMoves } from './listKeys';
import { rowMenu } from './rowMenus';
import { afterRemoving, itemKey, itemName, movedIndex, rowsOf, settingsKey, shownRows, type Row } from './rows';
import './list.css';

export interface ItemListProps {
  data: WindowData;
  selected: string;
  /** A row chosen: by the keys, which keep the list open, or by a click or Enter, which fold a narrow one. */
  onSelect: (key: string, how: 'keys' | 'pointer') => void;
  /** Enter on a row: into the pane. */
  onEnter: (key: string) => void;
  folded: ReadonlySet<string>;
  onFold: (key: string, open: boolean) => void;
  onEditLabel: (item: string) => void;
  onAddItem: () => void;
  announce: (text: string) => void;
  /** An item removed, by its name, which a toast says with its Undo. */
  onRemoved: (name: string) => void;
  /** An edit of the overlays themselves, which moves the selection and says so. */
  onOverlayEdit: (edit: Edit) => void;
  /** The item whose value is pointed at on the real overlay, whose row shows it. */
  pointed: string | null;
  /** An item's row pointed at, or none, which marks its value on the real overlay. */
  onPoint: (item: string | null) => void;
}

const overlayKinds: ReadonlySet<Edit['kind']> = new Set(['newOverlay', 'duplicateOverlay', 'mergeOverlay', 'deleteOverlay']);

/**
 * Puts the focus on a row. A row that an edit moves is drawn again once the data comes back, which drops its focus,
 * so a move asks to wait for that data and the focus goes back to the row then.
 */
function useRowFocus(version: number): (key: string, waitForData?: boolean) => void {
  const pending = useRef<{ key: string; version: number } | null>(null);
  useLayoutEffect(() => {
    const wanted = pending.current;
    if (wanted === null) {
      return;
    }
    const row = document.querySelector<HTMLElement>(`[data-row="${CSS.escape(wanted.key)}"]`);
    if (row !== null && document.activeElement !== row && !(document.activeElement instanceof HTMLInputElement)) {
      row.focus();
    }
    if (version > wanted.version) {
      pending.current = null;
    }
  });
  return (key, waitForData = false) => {
    pending.current = waitForData ? { key, version } : null;
    document.querySelector<HTMLElement>(`[data-row="${CSS.escape(key)}"]`)?.focus();
  };
}

/** Where a dragged item lands on a row: before an item, or as a pill of its own at the top of a pill. */
function dropOf(data: WindowData, dragged: string, row: Row, event: DragEvent<HTMLDivElement>): Edit[] {
  const overlay = data.settings.overlays[row.overlay];
  const target = row.kind === 'overlay' ? undefined : row.items[0];
  if (overlay === undefined || target === dragged) {
    return [];
  }
  const from = overlay.items.findIndex((item) => item.id === dragged);
  const at = target === undefined ? overlay.items.length : overlay.items.findIndex((item) => item.id === target);
  const index = from >= 0 && from < at ? at - 1 : at;
  const box = event.currentTarget.getBoundingClientRect();
  const apart = row.kind === 'pill' && event.clientY < box.top + box.height / 2;
  const moved: Edit[] = from === index ? [] : [{ kind: 'moveItem', item: dragged, overlay: row.overlay, index }];
  return apart ? [...moved, { kind: 'item', item: dragged, field: 'own', value: true }] : moved;
}

export function ItemList({ data, selected, onSelect, onEnter, folded, onFold, onEditLabel, onAddItem, announce, onRemoved, onOverlayEdit, pointed, onPoint }: ItemListProps) {
  const t = useText();
  const edit = useEdit();
  const shownDisplays = displays(useMonitors(data.version));
  const rows = rowsOf(data.settings, data.view, folded, t);
  const shown = shownRows(rows);
  const [renaming, setRenaming] = useState<string | null>(null);
  const [menu, setMenu] = useState<{ row: Row; at: HTMLElement } | null>(null);
  const dragged = useRef<string | null>(null);
  const moved = useRef<string | null>(null);
  const focusRow = useRowFocus(data.version);

  useLayoutEffect(() => {
    const row = rows.find((one) => one.key === moved.current);
    const pill = rows.find((one) => one.key === row?.parent);
    if (row !== undefined && pill !== undefined) {
      moved.current = null;
      announce(t('UiText.ItemMoved', row.name, pill.items.indexOf(row.items[0] ?? '') + 1, pill.items.length, pill.name));
    }
  });

  const go = (row: Row): void => {
    onSelect(row.key, 'keys');
    focusRow(row.key);
  };
  const remove = (id: string): void => {
    const next = afterRemoving(data.settings, id);
    const gone = data.settings.overlays.flatMap((overlay) => overlay.items).find((item) => item.id === id);
    void edit({ kind: 'removeItem', item: id }).then((refused) => {
      onSelect(next, 'keys');
      focusRow(next);
      if (refused === null && gone !== undefined) {
        onRemoved(itemName(gone));
      }
    });
  };
  const run = (one: Edit, row: Row): void => {
    if (one.kind === 'removeItem') {
      remove(one.item);
      return;
    }
    if (overlayKinds.has(one.kind) || (one.kind === 'overlay' && one.field === 'hidden')) {
      onOverlayEdit(one);
      return;
    }
    focusRow(row.key, true);
    void edit(one);
  };
  const moves: ListMoves = {
    shown,
    go,
    fold: (row, open) => onFold(row.key, open),
    enter: (row) => onEnter(row.key),
    rename: (row) => (row.kind === 'pill' ? onEditLabel(row.items[0] ?? '') : setRenaming(row.key)),
    move: (row, by) => {
      const to = row.kind === 'item' ? movedIndex(data.settings, row.items[0] ?? '', by) : null;
      if (to !== null) {
        moved.current = row.key;
        focusRow(row.key, true);
        void edit({ kind: 'moveItem', item: row.items[0] ?? '', overlay: to.overlay, index: to.index });
      }
    },
    remove: (row) => {
      if (row.kind === 'item') {
        remove(row.items[0] ?? '');
      } else if (row.kind === 'overlay' && data.settings.overlays.length > 1) {
        onOverlayEdit({ kind: 'deleteOverlay', overlay: row.overlay });
      }
    },
    nudge: (row, dx, dy) => {
      focusRow(row.key, true);
      void edit({ kind: 'nudgeOverlay', overlay: row.overlay, dx, dy });
    },
    menu: (row, at) => setMenu({ row, at }),
  };
  const keyDown = (row: Row, event: KeyboardEvent<HTMLDivElement>): void => {
    if (event.target === event.currentTarget && rowKey(row, event, moves)) {
      event.preventDefault();
      event.stopPropagation();
    }
  };
  const rename = (row: Row, name: string | null): void => {
    setRenaming(null);
    focusRow(row.key);
    if (name !== null && name.trim() !== '' && name !== row.name) {
      void edit(row.kind === 'item' ? { kind: 'item', item: row.items[0] ?? '', field: 'name', value: name.trim() } : { kind: 'overlay', overlay: row.overlay, field: 'name', value: name.trim() });
    }
  };
  const drop = (row: Row, event: DragEvent<HTMLDivElement>): void => {
    event.preventDefault();
    const id = dragged.current;
    dragged.current = null;
    const edits = id === null ? [] : dropOf(data, id, row, event);
    if (id !== null && edits.length > 0) {
      // In order: the item moves before it gets its own pill.
      void asOneStep(() => edits.reduce<Promise<unknown>>((done, one) => done.then(() => edit(one)), Promise.resolve())).then(() => focusRow(itemKey(id), true));
    }
  };

  const tabStop = shown.some((row) => row.key === selected) ? selected : (shown[0]?.key ?? '');
  return (
    <div className="t-list">
      <div className="t-list-head">
        <Button icon={<Icon name="add" />} keys="Control+N" onClick={onAddItem}>
          {t('UiText.AddItem')}
        </Button>
      </div>
      <div className="t-tree" role="tree" aria-label={t('UiText.StageItems')}>
        <div className="t-rows" role="none">
          {shown.map((row) => (
            <ListRow
              key={row.key}
              row={row}
              selected={row.key === selected}
              tabStop={row.key === tabStop}
              renaming={row.key === renaming}
              onClick={(one) => onSelect(one.key, 'pointer')}
              onMenu={(one, at) => {
                go(one);
                setMenu({ row: one, at });
              }}
              pointed={row.kind === 'item' && row.items[0] === pointed}
              onPoint={(one) => onPoint(one?.kind === 'item' ? (one.items[0] ?? null) : null)}
              onKeyDown={keyDown}
              onFold={(one, open) => onFold(one.key, open)}
              onRename={rename}
              onStartRename={(one) => (one.kind === 'pill' ? undefined : setRenaming(one.key))}
              onDragStart={(one, event) => {
                dragged.current = one.items[0] ?? null;
                event.dataTransfer.effectAllowed = 'move';
              }}
              onDrop={drop}
            />
          ))}
        </div>
        <div
          role="treeitem"
          data-row={settingsKey}
          aria-level={1}
          aria-selected={selected === settingsKey}
          aria-keyshortcuts="Control+Comma"
          tabIndex={0}
          className={`t-row t-settings${selected === settingsKey ? ' t-selected' : ''}`}
          onClick={() => onSelect(settingsKey, 'pointer')}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              onEnter(settingsKey);
            }
          }}
        >
          <Icon name="settings" />
          <span className="t-name">{t('UiText.SettingsPage')}</span>
        </div>
      </div>
      {menu !== null && <Menu open at={menu.at} entries={rowMenu(menu.row, data.settings, t, { rename: (row) => setRenaming(row.key), editLabel: onEditLabel, fold: (row, open) => onFold(row.key, open), edit: (one) => run(one, menu.row), displays: shownDisplays })} onClose={() => setMenu(null)} />}
    </div>
  );
}
