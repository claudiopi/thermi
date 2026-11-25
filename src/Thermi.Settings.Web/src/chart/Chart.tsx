import { useLayoutEffect, useRef, useState, type MouseEvent, type PointerEvent, type ReactNode, type RefObject } from 'react';
import type { ChartEmpty, ChartView, Edit, Line } from '../bridge/bridge.g';
import { bridge } from '../bridge/useWindow';
import { Creature } from '../app/Creature';
import { useMeasured } from '../app/measured';
import { Button, Icon } from '../kit';
import { useText } from '../texts/Texts';
import { chartHeight, knobColumn, knobTops, levelAt, nearestLine, plotPaths, stopsOf, tintsOf, valueAt, yOf, type Plot } from './geometry';
import { Knob } from './Knob';
import { ScrubLabel, ScrubMarks, useScrub } from './Scrub';
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
const adds = { warning: 'UiText.AddWarningLine', critical: 'UiText.AddCriticalLine' } as const;
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

/** The warning and critical zones, faint, under the line. */
function Tints({ plot }: { plot: Plot }) {
  return (
    <>
      {tintsOf(plot).map((tint) => (
        <rect key={tint.level} className={`t-tint-${tint.level}`} x="0" y={tint.top} width={plot.width} height={tint.height} />
      ))}
    </>
  );
}

/** Why there is no line: the mark asleep beside the reason, over the plot. */
function EmptyNote({ why, width }: { why: ChartEmpty; width: number }) {
  const t = useText();
  return (
    <p className="t-chart-empty" style={{ width: `${width}px` }}>
      <Creature state="calm" className="t-chart-empty-mark" />
      <span>{t(empties[why])}</span>
    </p>
  );
}

type Held = Pick<ChartView, 'from' | 'to' | 'fromText' | 'toText'>;

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
  const live = chart.empty === null;
  const point = live ? scrub.ago : null;
  const queue = useRef<Promise<unknown> | null>(null);
  const later = (work: () => Promise<unknown>): void => {
    queue.current = (queue.current ?? Promise.resolve()).then(work);
  };
  const move = (line: Line, value: number | null): void => {
    if (value === null) {
      focus.after(`add-${line}`);
    }
    later(() => edit({ kind: 'line', item, line, value }));
  };
  const drag = (line: Line, event: PointerEvent<HTMLElement>, open: () => void): void => {
    const target = event.currentTarget;
    const top = target.closest('.t-chart-row')?.getBoundingClientRect().top ?? 0;
    const startY = event.clientY;
    let dragging = false;
    let shown = line === 'warning' ? chart.warning : chart.critical;
    target.setPointerCapture(event.pointerId);
    const onMove = (moving: globalThis.PointerEvent): void => {
      if (!dragging && Math.abs(moving.clientY - startY) < 3) {
        return;
      }
      if (!dragging) {
        dragging = true;
        setHeld({ from: chart.from, to: chart.to, fromText: chart.fromText, toText: chart.toText });
        later(() => bridge()?.gesture(true) ?? Promise.resolve());
      }
      const value = valueAt(plot, moving.clientY - top);
      if (value !== shown) {
        shown = value;
        later(() => edit({ kind: 'line', item, line, value }));
      }
    };
    const onUp = (): void => {
      target.removeEventListener('pointermove', onMove);
      target.removeEventListener('pointerup', onUp);
      if (dragging) {
        setHeld(null);
        later(() => bridge()?.gesture(false) ?? Promise.resolve());
      } else {
        open();
      }
    };
    target.addEventListener('pointermove', onMove);
    target.addEventListener('pointerup', onUp);
  };
  // The keys move a line through its knob; a press on the plot is the pointer's shortcut to the nearest one.
  const pressPlot = (event: MouseEvent<SVGSVGElement>): void => {
    // The press would otherwise drop the focus on the page, after the knob took it.
    event.preventDefault();
    const y = event.clientY - event.currentTarget.getBoundingClientRect().top;
    const line = metered ? nearestLine(plot, y) : null;
    if (line !== null) {
      focus.after(`knob-${line}`);
      later(() => edit({ kind: 'line', item, line, value: valueAt(plot, y) }));
    }
  };

  const tops = knobTops(plot);
  const paths = plotPaths(plot);
  const now = [...chart.readings].reverse().find((value) => value !== null);
  return (
    <section className="t-chart" ref={box}>
      {chart.title !== null && <div className="t-chart-title">{chart.title}</div>}
      <div className="t-chart-row" style={{ height: `${size.height}px` }}>
        {/* A slider while it has readings, as screen readers pass it the arrows, Home and End that move its point, where an
            image's they keep. */}
        <svg
          className="t-plot"
          role={live ? 'slider' : 'img'}
          aria-label={t('UiText.ChartOf', name)}
          aria-description={chart.summary}
          aria-valuemin={live ? 0 : undefined}
          aria-valuemax={live ? chart.points.length - 1 : undefined}
          aria-valuenow={live ? (scrub.heard?.at ?? chart.points.length - 1) : undefined}
          aria-valuetext={live ? (scrub.heard?.said ?? t('UiText.ChartNow')) : undefined}
          width={plot.width}
          height={plot.height}
          tabIndex={live ? 0 : -1}
          onMouseDown={pressPlot}
          {...scrub.handlers}
        >
          <defs>
            {/* By height, so each part of the line and its fill takes the level of the values it draws. */}
            <linearGradient id={`t-line-${item}`} gradientUnits="userSpaceOnUse" x1="0" x2="0" y1={yOf(plot, chart.from)} y2={yOf(plot, chart.to)}>
              {stopsOf(chart).map((stop, index) => (
                <stop key={`${stop.level}-${index % 2 === 0 ? 'from' : 'to'}`} offset={stop.offset} style={{ stopColor: `var(--thermi-${stop.level})` }} />
              ))}
            </linearGradient>
            {/* The fill fades from the line down to nothing, over its own height whatever the values. */}
            <linearGradient id={`t-fade-${item}`} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0" stopColor="#ffffff" stopOpacity="0.22" />
              <stop offset="1" stopColor="#ffffff" stopOpacity="0" />
            </linearGradient>
            <mask id={`t-fill-${item}`} maskContentUnits="objectBoundingBox">
              <rect width="1" height="1" fill={`url(#t-fade-${item})`} />
            </mask>
          </defs>
          {chart.empty === null && (
            <>
              <Tints plot={plot} />
              <path d={paths.fill} fill={`url(#t-line-${item})`} mask={`url(#t-fill-${item})`} />
              <path className="t-line" d={paths.line} stroke={`url(#t-line-${item})`} />
              {now !== undefined && <circle cx={plot.width} cy={yOf(plot, now)} r="3.5" style={{ fill: `var(--thermi-${levelAt(chart, now)})` }} />}
            </>
          )}
          {lines.map((one) => {
            const value = chart[one];
            return value === null ? null : (
              <line key={one} className={`t-threshold t-${one}`} x1="0" x2={plot.width} y1={yOf(plot, value)} y2={yOf(plot, value)} />
            );
          })}
          {point !== null && <ScrubMarks plot={plot} ago={point} />}
        </svg>
        {point !== null && <ScrubLabel plot={plot} ago={point} />}
        {chart.empty !== null && <EmptyNote why={chart.empty} width={plot.width} />}
        {chart.empty === null && (
          // The span's ends, quiet in the plot's corner, so the line's height reads as a value without a grid.
          <div className="t-scale" aria-hidden="true">
            <span>{chart.toText}</span>
            <span>{chart.fromText}</span>
          </div>
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
                text={chart[one === 'warning' ? 'warningText' : 'criticalText'] ?? ''}
                max={chart.max}
                step={chart.step}
                top={top}
                onMove={(next) => move(one, next)}
                onTyped={(typed) => {
                  if (typed.trim() === '') {
                    focus.after(`add-${one}`);
                  }
                  return edit({ kind: 'typedLine', item, line: one, typed });
                }}
                onPress={(event, open) => drag(one, event, open)}
                onFocus={() => focus.focused(`knob-${one}`)}
              />
            );
          })}
        </div>
      </div>
      <div className="t-chart-foot" style={{ width: `${plot.width}px` }}>
        {chart.empty === null && (
          <div className="t-axis" aria-hidden="true">
            <span>{chart.startText}</span>
            <span>{t('UiText.ChartNow')}</span>
          </div>
        )}
        {(chart.stats !== null || options !== undefined) && (
          <div className="t-stats-row">
            <div className="t-stats">{chart.stats}</div>
            {options}
          </div>
        )}
        {chart.fires !== null && (
          <div className="t-fires">
            <span>{t('UiText.FiresMostly')}</span>
            <Button
              className="t-small-button"
              onClick={() => {
                focus.after('knob-warning');
                later(() => edit({ kind: 'resetLines', item }));
              }}
            >
              {chart.fires}
            </Button>
          </div>
        )}
        <div className="t-pushed" aria-live="polite">
          {chart.pushed ?? ''}
        </div>
        {metered && lines.some((one) => chart[one] === null) && (
          <div className="t-adds">
            {lines.map((one) =>
              chart[one] !== null ? null : (
                <Button
                    key={one}
                    data-focus={`add-${one}`}
                    onFocus={() => focus.focused(`add-${one}`)}
                    className="t-small-button"
                    icon={<Icon name="add" />}
                    onClick={() => {
                      focus.after(`knob-${one}`);
                      later(() => edit({ kind: 'addLine', item, line: one }));
                    }}
                  >
                    {t(adds[one])}
                  </Button>
              ),
            )}
          </div>
        )}
      </div>
    </section>
  );
}
