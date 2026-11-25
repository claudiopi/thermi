import { useState, type KeyboardEvent, type PointerEvent } from 'react';
import { useText } from '../texts/Texts';
import { agoOf, levelAt, readingAt, xOf, yOf, type Plot } from './geometry';

/** A point of the chart, as seconds before now, so it stays on its place across the plot as each second's reading comes. */
interface Scrub {
  ago: number | null;
  /** What a screen reader hears: the point the keys moved to, never one under the pointer. */
  spoken: string;
  handlers: {
    onPointerMove: (event: PointerEvent<SVGSVGElement>) => void;
    onPointerLeave: () => void;
    onKeyDown: (event: KeyboardEvent<SVGSVGElement>) => void;
    onBlur: () => void;
  };
}

export function useSaidPoint(plot: Plot): (ago: number) => string {
  const t = useText();
  return (ago) => {
    const said = plot.chart.readingTexts[plot.chart.readings.length - 1 - ago] ?? t('UiText.ChartNoReading');
    return t('UiText.ChartPoint', said, t(...agoOf(ago)));
  };
}

export function useScrub(plot: Plot, say: (ago: number) => string): Scrub {
  const [ago, setAgo] = useState<number | null>(null);
  const [spoken, setSpoken] = useState('');
  const last = plot.chart.readings.length - 1;
  const hide = (): void => {
    setAgo(null);
    setSpoken('');
  };
  // The dot at an end draws past the plot, and a pointer on it past the first or last reading.
  const held = (seconds: number): number => Math.min(last, Math.max(0, seconds));
  const keyDown = (event: KeyboardEvent<SVGSVGElement>): void => {
    if (event.key === 'Escape' && ago !== null) {
      event.stopPropagation();
      hide();
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
    setSpoken(say(held(next)));
  };
  return {
    ago: ago === null || last < 0 ? null : Math.min(ago, last),
    spoken,
    handlers: {
      onPointerMove: (event) => {
        if (last >= 0) {
          setAgo(held(last - readingAt(plot, event.clientX - event.currentTarget.getBoundingClientRect().left)));
        }
      },
      onPointerLeave: hide,
      onKeyDown: keyDown,
      onBlur: hide,
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
export function ScrubLabel({ plot, ago, label }: { plot: Plot; ago: number; label: string }) {
  const x = xOf(plot, plot.chart.readings.length - 1 - ago);
  const right = x < plot.width / 2;
  return (
    <div
      role="tooltip"
      className={`t-scrub-label${right ? '' : ' t-scrub-left'}`}
      style={{ left: `${right ? x + 6 : x - 6}px`, maxWidth: `${Math.max(0, plot.width / 2 - 6)}px` }}
    >
      {label}
    </div>
  );
}
