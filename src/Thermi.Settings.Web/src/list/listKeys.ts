import type { KeyboardEvent } from 'react';
import { rowStarting, type Row } from './rows';

/** What the list's keys do, which the list wires to its state and the bridge. */
export interface ListMoves {
  shown: readonly Row[];
  /** Selects a row and puts the focus on it: the arrows select as they move. */
  go: (row: Row) => void;
  fold: (row: Row, open: boolean) => void;
  enter: (row: Row) => void;
  rename: (row: Row) => void;
  move: (row: Row, by: -1 | 1) => void;
  /** Moves a floating overlay by the pixels given. */
  nudge: (row: Row, dx: number, dy: number) => void;
  remove: (row: Row) => void;
  menu: (row: Row, at: HTMLElement) => void;
}

const typeAheadPause = 1000;
/** Alt+arrows move a floating overlay this far, 1 px with Shift (R148). */
const nudgeStep = 8;
const arrows: Record<string, readonly [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
let typed = { text: '', at: Number.NEGATIVE_INFINITY };

function sideways(row: Row, index: number, moves: ListMoves, right: boolean): void {
  if (right) {
    const child = moves.shown[index + 1];
    if (row.branch && !row.expanded) {
      moves.fold(row, true);
    } else if (row.branch && child?.parent === row.key) {
      moves.go(child);
    }
    return;
  }
  const parent = moves.shown.find((one) => one.key === row.parent);
  if (row.branch && row.expanded) {
    moves.fold(row, false);
  } else if (parent !== undefined) {
    moves.go(parent);
  }
}

function typeAhead(row: Row, index: number, event: KeyboardEvent<HTMLElement>, moves: ListMoves): boolean {
  const letter = event.key.length === 1 && !event.ctrlKey && !event.altKey && !event.metaKey;
  // ? opens the shortcuts, as F1 does, so no name is found by it.
  const fresh = event.timeStamp - typed.at > typeAheadPause;
  if (!letter || event.key === '?' || (event.key === ' ' && (fresh || typed.text === ''))) {
    return false;
  }
  typed = { text: fresh ? event.key : typed.text + event.key, at: event.timeStamp };
  const found = rowStarting(moves.shown, index, typed.text);
  if (found !== null && found.key !== row.key) {
    moves.go(found);
  }
  return true;
}

function plainKey(row: Row, index: number, key: string, moves: ListMoves): boolean {
  const { shown } = moves;
  const to = (target: Row | undefined): void => {
    if (target !== undefined) {
      moves.go(target);
    }
  };
  const handlers: Record<string, () => void> = {
    ArrowDown: () => to(shown[index + 1]),
    ArrowUp: () => to(shown[index - 1]),
    Home: () => to(shown[0]),
    End: () => to(shown.at(-1)),
    ArrowLeft: () => sideways(row, index, moves, false),
    ArrowRight: () => sideways(row, index, moves, true),
    Enter: () => moves.enter(row),
    F2: () => moves.rename(row),
    Delete: () => moves.remove(row),
  };
  const handler = handlers[key];
  handler?.();
  return handler !== undefined;
}

/** The list's keys on a row; true when the key was the list's. */
export function rowKey(row: Row, event: KeyboardEvent<HTMLElement>, moves: ListMoves): boolean {
  const index = moves.shown.findIndex((one) => one.key === row.key);
  if ((event.shiftKey && event.key === 'F10') || event.key === 'ContextMenu') {
    moves.menu(row, event.currentTarget);
    return true;
  }
  const arrow = arrows[event.key];
  if (event.altKey && row.kind === 'overlay' && arrow !== undefined) {
    const step = event.shiftKey ? 1 : nudgeStep;
    moves.nudge(row, arrow[0] * step, arrow[1] * step);
    return true;
  }
  if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
    moves.move(row, event.key === 'ArrowUp' ? -1 : 1);
    return true;
  }
  if (event.ctrlKey || event.altKey || event.metaKey) {
    return false;
  }
  return plainKey(row, index, event.key, moves) || typeAhead(row, index, event, moves);
}
