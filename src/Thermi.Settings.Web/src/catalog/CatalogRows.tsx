import { useEffect, useRef, useState, type KeyboardEvent, type MouseEvent, type Ref } from 'react';
import type { CatalogGroup, CatalogRow, NoValue } from '../bridge/bridge.g';
import { Icon, Tooltip } from '../kit';
import { useText, type TextKey } from '../texts/Texts';
import { Sparkline } from './Sparkline';

export interface CatalogRowsProps {
  groups: readonly CatalogGroup[];
  /** The names of the items that show a sensor already. */
  shownBy: (sensor: string) => string[];
  /** Enter or a click adds; with Ctrl the catalog stays open. */
  onPick: (sensor: string, keep: boolean) => void;
  /** A sensor's new name, empty to give back the library's. */
  onRename: (sensor: string, name: string) => void;
  /** Up from the first row goes back to the search. */
  onLeaveUp: () => void;
  ref?: Ref<HTMLDivElement>;
}

const reasons: Record<NoValue, TextKey> = {
  'driver-may-be-missing': 'CatalogText.RowNeedsDriver',
  stopped: 'CatalogText.RowStopped',
  resting: 'CatalogText.RowResting',
  'reads-nothing': 'CatalogText.RowReadsNothing',
};

// A drive, an adapter or a battery is named under its group, as "Network · Intel I226-V".
const groupKeys: Partial<Record<NonNullable<CatalogGroup['kind']>, TextKey>> = {
  network: 'CatalogText.GroupNetwork',
  ssd: 'CatalogText.GroupDisk',
  battery: 'CatalogText.GroupBattery',
};

const kindKey = (kind: CatalogRow['kind']): TextKey => `SensorKind.${kind.charAt(0).toUpperCase()}${kind.slice(1)}`;

function moveFocus(event: KeyboardEvent<HTMLDivElement>, onLeaveUp: () => void): boolean {
  const rows = [...(event.currentTarget.closest('[role="listbox"]')?.querySelectorAll<HTMLElement>('[role="option"]') ?? [])];
  const at = rows.indexOf(event.currentTarget);
  const to: Record<string, number> = { ArrowDown: at + 1, ArrowUp: at - 1, Home: 0, End: rows.length - 1, PageDown: at + 8, PageUp: at - 8 };
  const next = to[event.key];
  if (next === undefined) {
    return false;
  }
  if (next < 0 && event.key === 'ArrowUp') {
    onLeaveUp();
  } else {
    rows[Math.max(0, Math.min(rows.length - 1, next))]?.focus();
  }
  return true;
}

/** The box that renames a sensor in its row: Enter or leaving it keeps the name, Esc puts it back. */
function NameBox({ row, onDone }: { row: CatalogRow; onDone: (name: string | null) => void }) {
  const t = useText();
  const box = useRef<HTMLInputElement>(null);
  const done = useRef(false);
  useEffect(() => {
    box.current?.focus();
    box.current?.select();
  }, []);
  const finish = (kept: string | null): void => {
    if (!done.current) {
      done.current = true;
      onDone(kept);
    }
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    event.stopPropagation();
    if (event.key === 'Enter') {
      event.preventDefault();
      finish(event.currentTarget.value);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      finish(null);
    }
  };
  return (
    <input
      ref={box}
      className="t-rename t-cat-rename-box"
      aria-label={t('CatalogText.SensorNameBox', row.name)}
      defaultValue={row.name}
      maxLength={40}
      onKeyDown={handleKeyDown}
      onBlur={(event) => finish(event.currentTarget.value)}
    />
  );
}

/**
 * The catalog's sensors, a group per device: each row with its name, why it reads nothing, a probable name's note or
 * which items show it, its last minute and its value now. The arrows move through the rows, Enter adds the one focused,
 * and F2 or its pencil renames its sensor.
 */
export function CatalogRows({ groups, shownBy, onPick, onRename, onLeaveUp, ref }: CatalogRowsProps) {
  const t = useText();
  const [renaming, setRenaming] = useState<string | null>(null);
  const focusRow = (sensor: string): void => {
    queueMicrotask(() => document.querySelector<HTMLElement>(`.t-cat-list [role="option"][data-sensor="${CSS.escape(sensor)}"]`)?.focus());
  };
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>, sensor: string): void => {
    if (moveFocus(event, onLeaveUp)) {
      event.preventDefault();
    } else if (event.key === 'F2') {
      event.preventDefault();
      setRenaming(sensor);
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onPick(sensor, event.ctrlKey);
    }
  };
  const handleClick = (event: MouseEvent<HTMLDivElement>, sensor: string): void => onPick(sensor, event.ctrlKey);
  const handlePencil = (event: MouseEvent<HTMLSpanElement>, sensor: string): void => {
    event.stopPropagation();
    setRenaming(sensor);
  };
  const handleRenamed = (row: CatalogRow, name: string | null): void => {
    setRenaming(null);
    focusRow(row.id);
    if (name !== null && name.trim() !== row.name) {
      onRename(row.id, name.trim());
    }
  };
  // Why it reads nothing comes first, then that its name is only likely, then the items that show it.
  const under = (row: CatalogRow): string => {
    if (row.why !== null) {
      return t(reasons[row.why]);
    }
    const names = shownBy(row.id);
    if (row.from !== 'likely' && names.length > 0) {
      return t('CatalogText.ShownAs', names.join(', '));
    }
    return row.note ?? t(kindKey(row.kind));
  };
  return (
    <div ref={ref} className="t-cat-list" role="listbox" aria-label={t('UiText.Catalog')}>
      {groups.map((group) => {
        const kind = group.kind === null ? undefined : groupKeys[group.kind];
        const heading = `t-cat-${group.id}`;
        return (
          <div key={group.id} role="group" aria-labelledby={heading}>
            <div id={heading} className="t-cat-dev">
              {kind === undefined ? group.name : t('UiText.SensorOn', t(kind), group.name)}
            </div>
            {group.sensors.map((row) => {
              if (row.id === renaming) {
                return (
                  <div key={row.id} className="t-cat-row t-cat-renaming" role="none">
                    <NameBox row={row} onDone={(name) => handleRenamed(row, name)} />
                  </div>
                );
              }
              const name = (
                <span className="t-cat-name">
                  <span className="t-cat-n">{row.name}</span>
                  <span className={`t-cat-d${row.why === null ? '' : ' t-cat-why'}`}>{under(row)}</span>
                </span>
              );
              return (
                <div
                  key={row.id}
                  className="t-cat-row"
                  role="option"
                  aria-selected={false}
                  aria-keyshortcuts="F2"
                  data-sensor={row.id}
                  tabIndex={-1}
                  onKeyDown={(event) => handleKeyDown(event, row.id)}
                  onClick={(event) => handleClick(event, row.id)}
                >
                  {/* A probable name says why in the kit's tooltip; the row itself stays put, so its focus does too. */}
                  {row.tip === null ? name : <Tooltip text={row.tip}>{name}</Tooltip>}
                  <Sparkline minute={row.minute} kind={row.kind} />
                  {/* A GPU's fan at rest reads a real 0 RPM, so it shows as any value, never dimmed as one off. */}
                  <span className={`t-cat-v${row.value === null || (row.why !== null && row.why !== 'resting') ? ' t-cat-off' : ''}`}>{row.value}</span>
                  {/* For the pointer alone: the keyboard has F2, which the row names, so the option holds no control of its own. */}
                  <Tooltip text={t('CatalogText.RenameSensor', row.name)}>
                    <span className="t-cat-pencil" aria-hidden="true" onClick={(event) => handlePencil(event, row.id)}>
                      <Icon name="edit" />
                    </span>
                  </Tooltip>
                </div>
              );
            })}
          </div>
        );
      })}
    </div>
  );
}
