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
  /** The overlays, by index, that do not fit beside the taskbar's apps, which the C# side tells when it changes. */
  crowded: number[];
  /** Counts each refresh, so what reads more from the bridge, like a chart, reads again. */
  version: number;
}

/** The bridge the host injects, or null in a plain browser, where the page shows nothing. */
export function bridge(): Bridge | null {
  return window.thermi ?? null;
}

async function read(from: Bridge, version: number): Promise<WindowData> {
  const [settings, view, sensors, texts, status, steps, problem, startWithWindows, crowded] = await Promise.all([
    from.settings(),
    from.view(),
    from.snapshot(),
    from.texts(),
    from.status(),
    from.steps(),
    from.problem(),
    from.startWithWindows(),
    from.crowded(),
  ]);
  return { settings, view, sensors, texts, status, steps, problem, startWithWindows, crowded, version };
}

/**
 * The window's data, read again each time the C# side says something changed: an edit, an undo, or new readings.
 * The page holds no copy of its own, so what it shows is always what the settings hold.
 */
export function useWindowData(): WindowData | null {
  const [data, setData] = useState<WindowData | null>(null);
  useEffect(() => {
    const from = bridge();
    if (from === null) {
      return;
    }
    let version = 0;
    let live = true;
    const refresh = (): void => {
      version += 1;
      const mine = version;
      void read(from, mine).then((next) => {
        if (live && mine === version) {
          setData(next);
        }
      });
    };
    from.changed(refresh);
    refresh();
    return () => {
      live = false;
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
 * What the bridge answers to `read` for `arg`, read again at each refresh of the window's data; null until it answers.
 * `read` must be the same function each time, so it is declared once, outside the component.
 */
export function useBridgeRead<A extends string | number, T>(read: (from: Bridge, arg: A) => Promise<T>, arg: A, version: number): T | null {
  const [answer, setAnswer] = useState<{ arg: A; value: T } | null>(null);
  useEffect(() => {
    let live = true;
    const from = bridge();
    if (from !== null) {
      void read(from, arg).then((value) => {
        if (live) {
          setAnswer({ arg, value });
        }
      });
    }
    return () => {
      live = false;
    };
  }, [read, arg, version]);
  return answer?.arg === arg ? answer.value : null;
}

const readChart = (from: Bridge, item: string): Promise<ChartView | null> => from.chart(item);

/** An item's chart, read again at each refresh of the window's data. */
export function useChart(item: string, version: number): ChartView | null {
  return useBridgeRead(readChart, item, version);
}
