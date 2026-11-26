import { useCallback, useEffect, useState } from 'react';
import type { Bridge, ChartView, Edit, Problem, SensorView, Settings, UndoSteps, WindowStatus, WindowView } from './bridge.g';

/** Everything the window shows, as the C# side last gave it. */
export interface WindowData {
  settings: Settings;
  view: WindowView;
  sensors: SensorView;
  texts: Record<string, string>;
  status: WindowStatus;
  steps: UndoSteps;
  /** Why the settings file cannot be used, or null; while it cannot, the window shows only that. */
  problem: Problem | null;
  /** Windows' Run entry, which is no part of the settings. */
  startWithWindows: boolean;
  /** Counts each refresh, new readings included, so what reads them from the bridge, like a chart, reads again. */
  version: number;
  /** Counts each refresh but new readings, so what changes only with the settings, the looks or the desktop reads again. */
  changes: number;
}

type Read = Omit<WindowData, 'version' | 'changes'>;
// The sensors themselves come with each change, which other sensors or the service starting or stopping are: a second's
// readings change only their values, which the view words.
type Readings = Pick<WindowData, 'view' | 'status'>;

/** The bridge the host injects, or null in a plain browser, where the page shows nothing. */
export function bridge(): Bridge | null {
  return window.thermi ?? null;
}

// What the person changed, as C# gets it: each change once the one before it is answered.
let changing: Promise<unknown> = Promise.resolve();

/**
 * Asks C# for `change` once every change asked before it is answered, so they apply in the order the person made them:
 * an undo pressed while a knob's moves are on their way undoes those moves, never only what came before them.
 */
export async function inOrder<T>(change: () => Promise<T>): Promise<T> {
  const answered = changing.then(change);
  changing = answered.catch(() => undefined);
  return answered;
}

/**
 * All the window shows, after a change. The texts, thousands of them, are read again only when the language changed,
 * and the sensors only when C# says they may have, or their names or the language changed: an edit, as each step of a
 * slider, changes neither.
 */
async function readAll(from: Bridge, before: Read | null, sensorsChanged: boolean): Promise<Read> {
  const [settings, view, status, steps, problem, startWithWindows] = await Promise.all([
    from.settings(),
    from.view(),
    from.status(),
    from.steps(),
    from.problem(),
    from.startWithWindows(),
  ]);
  const language = before !== null && before.settings.language === settings.language;
  const names = language && same(before.settings.sensorNames, settings.sensorNames);
  const [texts, sensors] = await Promise.all([
    language ? before.texts : from.texts(),
    names && !sensorsChanged ? before.sensors : from.snapshot(),
  ]);
  return { settings, view, sensors, texts, status, steps, problem, startWithWindows };
}

async function readReadings(from: Bridge): Promise<Readings> {
  const [view, status] = await Promise.all([from.view(), from.status()]);
  return { view, status };
}

/** Whether two answers of the bridge hold the same. */
const same = (one: unknown, other: unknown): boolean => one === other || JSON.stringify(one) === JSON.stringify(other);

/** `next`, or `before` where it holds the same, so what did not change keeps its identity and what shows it is not drawn again. */
const kept = <T>(before: T, next: T): T => (same(before, next) ? before : next);

/** The data after a second's readings: the same object when they changed nothing, which draws nothing. */
function withReadings(before: WindowData, next: Readings): WindowData {
  const view = kept(before.view, next.view);
  const status = kept(before.status, next.status);
  return view === before.view && status === before.status ? before : { ...before, view, status, version: before.version + 1 };
}

/** A read the page shows live, like a chart: read again with each second's readings, and shown in the same render as they. */
interface LiveRead {
  read: (from: Bridge) => Promise<unknown>;
  show: (value: unknown) => void;
}

const liveReads = new Set<LiveRead>();

/**
 * The window's data, read whole each time the C# side says something changed, an edit or an undo, and only what
 * readings change for each second's new readings, with the live reads, drawn once. The page holds no copy of its own,
 * so what it shows is always what the settings hold. A hidden page, minimized, reads nothing until it shows again.
 */
export function useWindowData(): WindowData | null {
  const [data, setData] = useState<WindowData | null>(null);
  useEffect(() => {
    const from = bridge();
    if (from === null) {
      return;
    }
    let changes = 0;
    let ticks = 0;
    let live = true;
    // What the page shows, and whether the sensors may have changed since: a refresh that gave way to a later one read
    // them for nothing, so the later one reads them again.
    let showing: Read | null = null;
    let sensorsChanged = true;
    const refresh = (sensors: boolean): void => {
      changes += 1;
      sensorsChanged ||= sensors;
      const mine = changes;
      const readsSensors = sensorsChanged;
      void readAll(from, showing, readsSensors).then((next) => {
        if (live && mine === changes) {
          showing = next;
          sensorsChanged &&= !readsSensors;
          setData((before) => ({ ...next, version: (before?.version ?? 0) + 1, changes: mine }));
        }
      });
    };
    // Readings asked for before a change came back are older than it, and give way to it. C# sends the view and the
    // status with each second's readings; a page that shows again asks for them.
    const tick = (sent: Readings | null): void => {
      if (document.hidden) {
        return;
      }
      ticks += 1;
      const mine = ticks;
      const since = changes;
      const reads = [...liveReads];
      void Promise.all([sent ?? readReadings(from), ...reads.map((one) => one.read(from))]).then(([next, ...values]) => {
        if (live && mine === ticks && since === changes) {
          reads.forEach((one, index) => one.show(values[index]));
          setData((before) => (before === null ? null : withReadings(before, next)));
        }
      });
    };
    const shown = (): void => tick(null);
    const stopChanges = from.changed((change) => refresh(change.sensors));
    const stopReadings = from.readings(tick);
    document.addEventListener('visibilitychange', shown);
    refresh(true);
    return () => {
      live = false;
      stopChanges();
      stopReadings();
      document.removeEventListener('visibilitychange', shown);
    };
  }, []);
  return data;
}

/** An edit, which comes back null once applied or with the reason it was refused, as a text key or worded. */
export function useEdit(): (edit: Edit) => Promise<string | null> {
  return useCallback(async (edit: Edit) => (await bridge()?.edit(edit)) ?? null, []);
}

/** Several edits as one undo step, like a drag or a move that also gives an item its own pill. */
export async function asOneStep(edits: () => Promise<unknown>): Promise<void> {
  const from = bridge();
  await from?.gesture(true);
  try {
    await edits();
  } finally {
    await from?.gesture(false);
  }
}

/**
 * What the bridge answers to `read` for `arg`, read again each time `version` changes and, when `live`, with each
 * second's readings too; null until it answers. An answer that holds what the last did keeps it, which draws nothing.
 * `read` must be the same function each time, so it is declared once, outside the component.
 */
export function useBridgeRead<A extends string | number, T>(read: (from: Bridge, arg: A) => Promise<T>, arg: A, version: number, live = false): T | null {
  const [answer, setAnswer] = useState<{ arg: A; value: T } | null>(null);
  useEffect(() => {
    let mounted = true;
    const from = bridge();
    if (from === null) {
      return undefined;
    }
    const show = (value: T): void => {
      if (mounted) {
        setAnswer((before) => (before?.arg === arg && same(before.value, value) ? before : { arg, value }));
      }
    };
    void read(from, arg).then(show);
    const each: LiveRead = { read: (source) => read(source, arg), show: (value) => show(value as T) };
    if (live) {
      liveReads.add(each);
    }
    return () => {
      mounted = false;
      liveReads.delete(each);
    };
  }, [read, arg, version, live]);
  return answer?.arg === arg ? answer.value : null;
}

const readChart = (from: Bridge, item: string): Promise<ChartView | null> => from.chart(item);

/** An item's chart, live, and read again at each change of the window's data. */
export function useChart(item: string, changes: number): ChartView | null {
  return useBridgeRead(readChart, item, changes, true);
}
