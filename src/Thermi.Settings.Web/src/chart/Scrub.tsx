import { useState, type KeyboardEvent, type PointerEvent } from 'react';
import { levelAt, readingAt, xOf, yOf, type Plot } from './geometry';

/** A point of the chart, as seconds before now, so it stays on its place across the plot as each second's reading comes. */
interface Scrub {
  ago: number | null;
  /** The point the keys last moved to, as a screen reader hears it, held as it was then, never one under the pointer. */
  heard: { ago: number; said: string } | null;
  handlers: {
    onPointerMove: (event: PointerEvent<SVGSVGElement>) => void;
    onPointerLeave: () => void;
    onKeyDown: (event: KeyboardEvent<SVGSVGElement>) => void;
    onBlur: () => void;
  };
}

export function useScrub(plot: Plot): Scrub {
  const [ago, setAgo] = useState<number | null>(null);
  const [heard, setHeard] = useState<Scrub['heard']>(null);
  const { points } = plot.chart;
  const last = points.length - 1;
  // The dot at an end draws past the plot, and a pointer on it past the first or last reading.
  const held = (seconds: number): number => Math.min(last, Math.max(0, seconds));
  const keyDown = (event: KeyboardEvent<SVGSVGElement>): void => {
    if (event.key === 'Escape' && ago !== null) {
      event.stopPropagation();
      setAgo(null);
      setHeard(null);
      return;
    }
    const from = ago ?? 0;
    const to: Record<string, number> = { ArrowLeft: from + 1, ArrowRight: from - 1, Home: last, End: 0 };
    const next = to[event.key];
    if (next === undefined || last < 0) {
      return;
    }
    event.preventDefault();
    setAgo(held(next));
    setHeard({ ago: held(next), said: points[last - held(next)] ?? '' });
  };
  return {
    ago: ago === null || last < 0 ? null : Math.min(ago, last),
    heard,
    handlers: {
      onPointerMove: (event) => {
        if (last >= 0) {
          setAgo(held(last - readingAt(plot, event.clientX - event.currentTarget.getBoundingClientRect().left)));
        }
      },
      onPointerLeave: () => setAgo(null),
      onKeyDown: keyDown,
      onBlur: () => {
        setAgo(null);
        setHeard(null);
      },
    },
  };
}

export function ScrubMarks({ plot, ago }: { plot: Plot; ago: number }) {
  const index = plot.chart.readings.length - 1 - ago;
  const value = plot.chart.readings[index] ?? null;
  const x = xOf(plot, index);
  return (
    <>
      <line className="t-scrub-line" x1={x} x2={x} y1="0" y2={plot.height} />
      {value !== null && (
        <circle className="t-scrub-dot" cx={x} cy={yOf(plot, value)} r="3.5" style={{ fill: `var(--thermi-${levelAt(plot.chart, value)})` }} />
      )}
    </>
  );
}

/** Sits on the side of its line with room, never past the plot's half. */
export function ScrubLabel({ plot, ago }: { plot: Plot; ago: number }) {
  const index = plot.chart.points.length - 1 - ago;
  const x = xOf(plot, index);
  const right = x < plot.width / 2;
  return (
    <div
      role="tooltip"
      className={`t-scrub-label${right ? '' : ' t-scrub-left'}`}
      style={{ left: `${right ? x + 6 : x - 6}px`, maxWidth: `${Math.max(0, plot.width / 2 - 6)}px` }}
    >
      {plot.chart.points[index]}
    </div>
  );
}
