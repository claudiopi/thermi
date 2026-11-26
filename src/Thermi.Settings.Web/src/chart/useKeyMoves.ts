import { useLayoutEffect, useRef } from 'react';
import type { ChartView, Line } from '../bridge/bridge.g';
import { inOrder } from '../bridge/useWindow';

interface Moved {
  value: number;
  /** What the chart showed for the line when C# kept the move, or undefined until then. */
  shownWhenKept?: number | null;
}

/**
 * Where the keys last moved each line, until the chart shows the move: a key pressed before then moves on from it, so a
 * second quick press is never lost to the line not having been drawn again yet. A move refused, or one kept and then
 * drawn, whatever the chart then shows, gives the line back to the chart.
 */
export function useKeyMoves(chart: ChartView): {
  from: (line: Line) => number | null;
  move: (line: Line, value: number | null, ask: () => Promise<string | null>) => void;
} {
  const moved = useRef(new Map<Line, Moved>());
  const shown = useRef(chart);
  useLayoutEffect(() => {
    shown.current = chart;
    for (const [line, one] of moved.current) {
      if (one.value === chart[line] || (one.shownWhenKept !== undefined && one.shownWhenKept !== chart[line])) {
        moved.current.delete(line);
      }
    }
  }, [chart]);
  const forget = (line: Line, one: Moved): void => {
    if (moved.current.get(line) === one) {
      moved.current.delete(line);
    }
  };
  return {
    from: (line) => moved.current.get(line)?.value ?? chart[line],
    move: (line, value, ask) => {
      const one: Moved | null = value === null ? null : { value };
      moved.current.delete(line);
      if (one !== null) {
        moved.current.set(line, one);
      }
      void inOrder(ask).then(
        (refusal) => {
          if (one !== null && refusal === null) {
            one.shownWhenKept = shown.current[line];
          } else if (one !== null) {
            forget(line, one);
          }
        },
        () => one !== null && forget(line, one),
      );
    },
  };
}
