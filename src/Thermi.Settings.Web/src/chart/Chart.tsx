import { useLayoutEffect, useRef, useState, type MouseEvent, type PointerEvent, type ReactNode, type RefObject } from 'react';
import type { ChartEmpty, ChartView, Edit, Line } from '../bridge/bridge.g';
import { bridge } from '../bridge/useWindow';
import { Creature } from '../app/Creature';
import { Button, Icon } from '../kit';
import { useText } from '../texts/Texts';
import { chartHeight, colorAt, fillPath, knobColumn, knobTops, linePath, nearestLine, stopsOf, valueAt, yOf, type Plot } from './geometry';
import { Knob } from './Knob';
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

/** The chart's width, from its column, and the pane's height, which its own height follows. */
function useSize(): [RefObject<HTMLDivElement | null>, { width: number; height: number }] {
  const box = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 480, height: 273 });
  useLayoutEffect(() => {
    const element = box.current;
    const pane = element?.closest('main');
    if (element === null || pane === null || pane === undefined) {
      return undefined;
    }
    const measure = (): void => {
      const width = element.clientWidth;
      // The item page lays the chart beside its cards from 864 px of pane on, as item.css says.
      setSize({ width, height: chartHeight(width, pane.clientHeight, pane.clientWidth >= besideFrom) });
    };
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    observer.observe(pane);
    return () => observer.disconnect();
  }, []);
  return [box, size];
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
  const { chart } = plot;
  const top = chart.critical === null ? 0 : yOf(plot, chart.critical);
  return (
    <>
      {chart.critical !== null && <rect className="t-tint-critical" x="0" y="0" width={plot.width} height={Math.max(0, top)} />}
      {chart.warning !== null && <rect className="t-tint-warning" x="0" y={top} width={plot.width} height={Math.max(0, yOf(plot, chart.warning) - top)} />}
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

/** An item's chart: its meter's last minutes, the lines with their knobs, what the minutes held, and its options. */
export function Chart({ item, name, chart, edit, metered, options }: ChartProps) {
  const t = useText();
  const [box, size] = useSize();
  const plot: Plot = { width: Math.max(0, size.width - knobColumn), height: size.height, chart };
  const focus = useFocusHandOver(chart);
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
  const line = linePath(plot);
  const now = [...chart.readings].reverse().find((value) => value !== null);
  return (
    <section className="t-chart" ref={box}>
      {chart.title !== null && <div className="t-chart-title">{chart.title}</div>}
      <div className="t-chart-row" style={{ height: `${size.height}px` }}>
        <svg
          className="t-plot"
          role="img"
          aria-label={t('UiText.ChartOf', name)}
          aria-description={chart.summary}
          width={plot.width}
          height={plot.height}
          onMouseDown={pressPlot}
        >
          <defs>
            <linearGradient id={`t-line-${item}`} gradientUnits="userSpaceOnUse" x1="0" x2="0" y1={yOf(plot, chart.from)} y2={yOf(plot, chart.to)}>
              {stopsOf(chart).map((stop) => (
                <stop key={`${stop.color}-${stop.offset}`} offset={stop.offset} style={{ stopColor: `var(--thermi-${stop.color})` }} />
              ))}
            </linearGradient>
            <linearGradient id={`t-fill-${item}`} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0" style={{ stopColor: 'var(--thermi-middle)', stopOpacity: 0.18 }} />
              <stop offset="1" style={{ stopColor: 'var(--thermi-cool)', stopOpacity: 0 }} />
            </linearGradient>
          </defs>
          {chart.empty === null && (
            <>
              <Tints plot={plot} />
              <path d={fillPath(plot)} fill={`url(#t-fill-${item})`} />
              <path className="t-line" d={line} stroke={`url(#t-line-${item})`} />
              {now !== undefined && <circle cx={plot.width} cy={yOf(plot, now)} r="3.5" style={{ fill: `var(--thermi-${colorAt(chart, now)})` }} />}
            </>
          )}
          {lines.map((one) => {
            const value = chart[one];
            return value === null ? null : (
              <line key={one} className={`t-threshold t-${one}`} x1="0" x2={plot.width} y1={yOf(plot, value)} y2={yOf(plot, value)} />
            );
          })}
        </svg>
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
