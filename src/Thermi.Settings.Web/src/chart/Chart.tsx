import { useLayoutEffect, useRef, useState, type MouseEvent, type PointerEvent, type ReactNode, type RefObject } from 'react';
import type { ChartEmpty, ChartView, Edit, Line } from '../bridge/bridge.g';
import { bridge, inOrder } from '../bridge/useWindow';
import { Creature } from '../app/Creature';
import { useMeasured } from '../app/measured';
import { useText } from '../texts/Texts';
import { ChartFoot } from './ChartFoot';
import { chartHeight, emptyBand, knobColumn, knobTops, nearestLine, valueAt, type Plot } from './geometry';
import { Knob } from './Knob';
import { useKeyMoves } from './useKeyMoves';
import { PlotDrawing } from './PlotDrawing';
import { ScrubLabel, useScrub } from './Scrub';
import './chart.css';

export interface ChartProps {
  item: string;
  name: string;
  chart: ChartView;
  edit: (edit: Edit) => Promise<string | null>;
  /** Whether the item has a meter, whose lines the chart sets; without one it draws the reading alone. */
  metered: boolean;
  /** The chart's own menu, on the line of its numbers, beside what it acts on. */
  options?: ReactNode;
}

const lines: Line[] = ['warning', 'critical'];
const names = { warning: 'UiText.WarningFrom', critical: 'UiText.CriticalFrom' } as const;
const texts = { warning: 'warningText', critical: 'criticalText' } as const;
const empties = { stopped: 'UiText.ChartSensorsStopped', driver: 'UiText.ChartNeedsDriver', none: 'UiText.ChartNoReadings' } as const;

/** The pane's width from which the item page lays the chart beside its cards. */
const besideFrom = 864;

function sizeOf(element: HTMLElement): { width: number; height: number } | null {
  const pane = element.closest('main');
  if (pane === null) {
    return null;
  }
  const width = element.clientWidth;
  // The item page lays the chart beside its cards from 864 px of pane on, as item.css says.
  return { width, height: chartHeight(width, pane.clientHeight, pane.clientWidth >= besideFrom) };
}

function useSize(): [RefObject<HTMLDivElement | null>, { width: number; height: number }] {
  const box = useRef<HTMLDivElement>(null);
  return [box, useMeasured(box, sizeOf, { width: 480, height: 273 })];
}

/**
 * Keeps the focus in the chart as its lines come and go: a knob turned off hands it to its line's Add button, and
 * the Add button, once its line is back, to the knob.
 */
function useFocusHandOver(chart: ChartView): { focused: (target: string) => void; after: (target: string) => void } {
  const last = useRef<string | null>(null);
  const wanted = useRef<string | null>(null);
  useLayoutEffect(() => {
    const lost = document.activeElement === document.body || document.activeElement === null;
    const swap = last.current?.startsWith('knob-') === true ? last.current.replace('knob-', 'add-') : last.current?.replace('add-', 'knob-');
    const target = wanted.current ?? (lost ? swap : null);
    // A knob asked for that the change left off hands over to its line's Add button.
    const find = (one: string): HTMLElement | null => document.querySelector<HTMLElement>(`[data-focus="${one}"]`);
    const element = target === null || target === undefined ? null : (find(target) ?? find(target.replace('knob-', 'add-')));
    if (element !== null) {
      element.focus();
      wanted.current = null;
    }
  }, [chart]);
  return {
    focused: (target) => {
      last.current = target;
    },
    after: (target) => {
      wanted.current = target;
    },
  };
}

/** Why there is no line: the mark asleep beside the reason, over the plot between its lines. */
function EmptyNote({ why, plot }: { why: ChartEmpty; plot: Plot }) {
  const t = useText();
  const band = emptyBand(plot);
  return (
    <p className="t-chart-empty" style={{ width: `${plot.width}px`, top: `${band.top}px`, height: `${band.height}px` }}>
      <Creature state="calm" className="t-chart-empty-mark" />
      <span>{t(empties[why])}</span>
    </p>
  );
}

type Held = Pick<ChartView, 'from' | 'to' | 'fromText' | 'toText'>;

const later = (work: () => Promise<unknown>): void => {
  void inOrder(work);
};
const gesture = (on: boolean): void => later(() => bridge()?.gesture(on) ?? Promise.resolve());

interface Drag {
  /** The chart as the press found it, whose span the drag holds. */
  chart: ChartView;
  plot: Plot;
  line: Line;
  hold: (held: Held | null) => void;
  set: (value: number) => void;
}

/** A press on a knob: past 3 px it drags the line, in one gesture; let go without a move, it opens the knob's box. */
function dragLine({ chart, plot, line, hold, set }: Drag, event: PointerEvent<HTMLElement>, open: () => void): void {
  const target = event.currentTarget;
  const top = target.closest('.t-chart-row')?.getBoundingClientRect().top ?? 0;
  const startY = event.clientY;
  let dragging = false;
  let shown = chart[line];
  target.setPointerCapture(event.pointerId);
  const onMove = (moving: globalThis.PointerEvent): void => {
    if (!dragging && Math.abs(moving.clientY - startY) < 3) {
      return;
    }
    if (!dragging) {
      dragging = true;
      hold({ from: chart.from, to: chart.to, fromText: chart.fromText, toText: chart.toText });
      gesture(true);
    }
    const value = valueAt(plot, moving.clientY - top);
    if (value !== shown) {
      shown = value;
      set(value);
    }
  };
  const onUp = (): void => {
    target.removeEventListener('pointermove', onMove);
    target.removeEventListener('pointerup', onUp);
    if (dragging) {
      hold(null);
      gesture(false);
    } else {
      open();
    }
  };
  target.addEventListener('pointermove', onMove);
  target.addEventListener('pointerup', onUp);
}

/** The plot's role: a slider while it has readings, as screen readers pass it the arrows, Home and End that move its point, where an image's they keep. */
function roleOf(chart: ChartView, heard: { at: number; said: string } | null, now: string) {
  const last = chart.points.length - 1;
  return chart.empty === null
    ? { role: 'slider', 'aria-valuemin': 0, 'aria-valuemax': last, 'aria-valuenow': heard?.at ?? last, 'aria-valuetext': heard?.said ?? now, tabIndex: 0 }
    : { role: 'img', tabIndex: -1 };
}

/**
 * An item's chart: its meter's last minutes, the lines with their knobs, what the minutes held, and its options. A drag
 * keeps the span the chart had when pressed: a line over every reading sets the span's top, which would move with it.
 */
export function Chart({ item, name, chart: given, edit, metered, options }: ChartProps) {
  const t = useText();
  const [box, size] = useSize();
  const [held, setHeld] = useState<Held | null>(null);
  const chart: ChartView = held === null ? given : { ...given, ...held };
  const plot: Plot = { width: Math.max(0, size.width - knobColumn), height: size.height, chart };
  const focus = useFocusHandOver(given);
  const scrub = useScrub(plot);
  const point = chart.empty === null ? scrub.ago : null;
  const keys = useKeyMoves(given);
  const setLine = (line: Line, value: number | null): Promise<string | null> => edit({ kind: 'line', item, line, value });
  const move = (line: Line, value: number | null): void => {
    if (value === null) {
      focus.after(`add-${line}`);
    }
    keys.move(line, value, () => setLine(line, value));
  };
  const typeLine = (line: Line, typed: string): Promise<string | null> => {
    if (typed.trim() === '') {
      focus.after(`add-${line}`);
    }
    return edit({ kind: 'typedLine', item, line, typed });
  };
  const drag = (line: Line, event: PointerEvent<HTMLElement>, open: () => void): void =>
    dragLine({ chart, plot, line, hold: setHeld, set: (value) => later(() => setLine(line, value)) }, event, open);
  // The keys move a line through its knob; a press on the plot is the pointer's shortcut to the nearest one.
  const pressPlot = (event: MouseEvent<SVGSVGElement>): void => {
    // The press would otherwise drop the focus on the page, after the knob took it.
    event.preventDefault();
    const y = event.clientY - event.currentTarget.getBoundingClientRect().top;
    const line = metered ? nearestLine(plot, y) : null;
    if (line !== null) {
      focus.after(`knob-${line}`);
      later(() => setLine(line, valueAt(plot, y)));
    }
  };

  const tops = knobTops(plot);
  return (
    <section className="t-chart" ref={box}>
      {chart.title !== null && <div className="t-chart-title">{chart.title}</div>}
      <div className="t-chart-row" style={{ height: `${size.height}px` }}>
        <svg
          className="t-plot"
          {...roleOf(chart, scrub.heard, t('UiText.ChartNow'))}
          aria-label={t('UiText.ChartOf', name)}
          aria-description={chart.summary}
          width={plot.width}
          height={plot.height}
          onMouseDown={pressPlot}
          {...scrub.handlers}
        >
          <PlotDrawing item={item} plot={plot} point={point} />
        </svg>
        {point !== null && <ScrubLabel plot={plot} ago={point} />}
        {chart.empty === null ? (
          // The span's ends, quiet in the plot's corner, so the line's height reads as a value without a grid.
          <div className="t-scale" aria-hidden="true">
            <span>{chart.toText}</span>
            <span>{chart.fromText}</span>
          </div>
        ) : (
          <EmptyNote why={chart.empty} plot={plot} />
        )}
        <div className="t-knobs">
          {lines.map((one) => {
            const value = chart[one];
            const top = tops[one];
            return value === null || top === null ? null : (
              <Knob
                key={one}
                line={one}
                label={t(names[one])}
                value={value}
                from={() => keys.from(one) ?? value}
                text={chart[texts[one]] ?? ''}
                max={chart.max}
                step={chart.step}
                top={top}
                onMove={(next) => move(one, next)}
                onTyped={(typed) => typeLine(one, typed)}
                onPress={(event, open) => drag(one, event, open)}
                onFocus={() => focus.focused(`knob-${one}`)}
              />
            );
          })}
        </div>
      </div>
      <ChartFoot
        chart={chart}
        width={plot.width}
        metered={metered}
        options={options}
        onReset={() => {
          focus.after('knob-warning');
          later(() => edit({ kind: 'resetLines', item }));
        }}
        onAdd={(one) => {
          focus.after(`knob-${one}`);
          later(() => edit({ kind: 'addLine', item, line: one }));
        }}
        onAddFocus={(one) => focus.focused(`add-${one}`)}
      />
    </section>
  );
}
