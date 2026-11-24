import { useEffect, useRef, type DragEvent, type KeyboardEvent, type MouseEvent } from 'react';
import type { TextKey } from '../texts/Texts';
import { Icon, steady } from '../kit';
import { useText } from '../texts/Texts';
import type { Row } from './rows';

export interface ListRowProps {
  row: Row;
  selected: boolean;
  /** Whether it is the list's Tab stop: the selected row, or the first while none shows. */
  tabStop: boolean;
  renaming: boolean;
  onClick: (row: Row) => void;
  /** A right-click: the row's menu, on the row. */
  onMenu: (row: Row, at: HTMLElement) => void;
  /** The pointer on the row or off it, which marks its value on the real overlay. */
  onPoint: (row: Row | null) => void;
  /** Its value pointed at on the real overlay. */
  pointed: boolean;
  onKeyDown: (row: Row, event: KeyboardEvent<HTMLDivElement>) => void;
  onFold: (row: Row, open: boolean) => void;
  onRename: (row: Row, name: string | null) => void;
  onStartRename: (row: Row) => void;
  onDragStart: (row: Row, event: DragEvent<HTMLDivElement>) => void;
  onDrop: (row: Row, event: DragEvent<HTMLDivElement>) => void;
}

/** The box that renames a row in place: Enter or leaving it keeps the name, Esc puts it back. */
function RenameBox({ row, label, onRename }: { row: Row; label: string; onRename: (row: Row, name: string | null) => void }) {
  const box = useRef<HTMLInputElement>(null);
  const done = useRef(false);
  useEffect(() => {
    box.current?.focus();
    box.current?.select();
  }, []);
  const finish = (kept: string | null): void => {
    if (!done.current) {
      done.current = true;
      onRename(row, kept);
    }
  };
  const keyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    event.stopPropagation();
    if (event.key === 'Enter') {
      finish(event.currentTarget.value);
    } else if (event.key === 'Escape') {
      finish(null);
    }
  };
  return (
    <input
      ref={box}
      className="t-rename"
      aria-label={label}
      defaultValue={row.name}
      onKeyDown={keyDown}
      onBlur={(event) => finish(event.currentTarget.value)}
      onClick={(event) => event.stopPropagation()}
    />
  );
}

/** The level an item's row shows, when it is past a line. */
function alertOf(row: Row): 'warning' | 'critical' | null {
  return row.alert === 'warning' || row.alert === 'critical' ? row.alert : null;
}

/** What a screen reader hears after an item's name: its value and, past a line, the level. */
function describe(row: Row, say: (key: TextKey) => string): string | undefined {
  const alert = alertOf(row);
  const parts = [row.value ?? '', alert === null ? '' : say(alert === 'warning' ? 'ChangedField.Warning' : 'ChangedField.Critical')].filter(
    (part) => part !== '',
  );
  return row.kind !== 'pill' && parts.length > 0 ? parts.join(', ') : undefined;
}

/** A row of the list: an overlay, a pill or an item, as the prototype draws each. */
function ListRowDrawn({
  row,
  selected,
  tabStop,
  renaming,
  onClick,
  onMenu,
  onPoint,
  pointed,
  onKeyDown,
  onFold,
  onRename,
  onStartRename,
  onDragStart,
  onDrop,
}: ListRowProps) {
  const t = useText();
  const fold = (event: MouseEvent): void => {
    event.stopPropagation();
    onFold(row, !row.expanded);
  };
  return (
    <div
      role="treeitem"
      data-row={row.key}
      className={classOf(row, selected, pointed)}
      aria-level={row.level}
      aria-selected={selected}
      aria-expanded={row.branch ? row.expanded : undefined}
      aria-label={renaming || row.kind === 'item' ? undefined : row.label}
      aria-description={describe(row, (key) => t(key))}
      tabIndex={tabStop ? 0 : -1}
      draggable={row.kind === 'item' && !renaming}
      onClick={() => onClick(row)}
      onContextMenu={(event) => {
        event.preventDefault();
        onMenu(row, event.currentTarget);
      }}
      onPointerEnter={() => onPoint(row)}
      onPointerLeave={() => onPoint(null)}
      onDoubleClick={() => onStartRename(row)}
      onKeyDown={(event) => onKeyDown(row, event)}
      onDragStart={(event) => onDragStart(row, event)}
      onDragOver={(event) => event.preventDefault()}
      onDrop={(event) => onDrop(row, event)}
    >
      {row.branch && (
        <span className="t-fold" aria-hidden="true" onClick={fold}>
          <Icon name={row.expanded ? 'chevronDown' : 'chevronRight'} />
        </span>
      )}
      {row.kind === 'item' && (
        <span className="t-grip" aria-hidden="true">
          <Icon name="grip" />
        </span>
      )}
      {renaming ? <RenameBox row={row} label={t('UiText.Rename')} onRename={onRename} /> : <span className="t-name">{row.name}</span>}
      <RowValue row={row} />
    </div>
  );
}

/** At the row's end, an item's value in its level's color, or "Hidden" on a hidden overlay's row; Narrator hears it as the description. */
function RowValue({ row }: { row: Row }) {
  if (row.kind === 'pill' || (row.kind === 'overlay' && row.value === undefined)) {
    return null;
  }
  const alert = alertOf(row);
  const mark = row.kind === 'overlay' ? ' t-hidden-mark' : alert === null ? '' : ` t-${alert}`;
  return (
    <span className={`t-value${mark}`} aria-hidden="true">
      {row.value ?? ''}
    </span>
  );
}

function classOf(row: Row, selected: boolean, pointed: boolean): string {
  const hidden = row.kind === 'overlay' && row.value !== undefined;
  return `t-row t-row-${row.kind}${selected ? ' t-selected' : ''}${pointed ? ' t-pointed' : ''}${hidden ? ' t-row-hidden' : ''}`;
}

export const ListRow = steady(ListRowDrawn);
