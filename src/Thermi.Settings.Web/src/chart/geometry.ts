import type { ChartColor, ChartView, Line } from '../bridge/bridge.g';

// Where the chart draws what the C# side gave it: its size from the pane, each value's height, the smooth line and
// where the knobs sit. The values, the range and the colors are the C# side's; this only places them.

/** The knobs' column beside the plot, as wide as the widest knob's text. */
export const knobColumn = 64;
/** Room above and under the line inside the plot, so the line and its dot never touch the edge. */
const pad = 12;
/** The least distance between two knobs' centers, so they never cover each other. */
const knobsApart = 26;

/** The room the item page's heading and the chart's foot take in the pane, above and under the plot. */
const aroundChart = 240;

/**
 * The chart's height: 40 % of the pane, at most half its width, between 140 and 640 px, so a large pane is not left half
 * empty. `beside` the cards, in a wide pane, nothing waits under it: it fills the pane's height but for its heading and
 * foot, at most 3/4 as high as wide.
 */
export function chartHeight(width: number, paneHeight: number, beside = false): number {
  const share = Math.max(140, Math.min(paneHeight * 0.4, width / 2, 640));
  return Math.round(beside ? Math.max(share, Math.min(paneHeight - aroundChart, width * 0.75)) : share);
}

export interface Plot {
  width: number;
  height: number;
  chart: ChartView;
}

export function yOf(plot: Plot, value: number): number {
  const { from, to } = plot.chart;
  const span = to - from || 1;
  return pad + ((to - value) / span) * (plot.height - 2 * pad);
}

/** The value at a height of the plot, on the knobs' steps and within their track. */
export function valueAt(plot: Plot, y: number): number {
  const { from, to, step, max } = plot.chart;
  const raw = to - ((y - pad) / (plot.height - 2 * pad)) * (to - from);
  return Math.min(max, Math.max(0, Math.round(raw / step) * step));
}

/** One stretch of the line between missing readings: its path and where it starts and ends across. */
interface Stretch {
  path: string;
  from: number;
  to: number;
}

// Smooth curves through the readings, a stretch for each run between missing ones.
function stretches(plot: Plot): Stretch[] {
  const { readings } = plot.chart;
  const last = Math.max(1, readings.length - 1);
  const points = readings.map((value, index) => (value === null ? null : ([(index / last) * plot.width, yOf(plot, value)] as const)));
  const runs: Stretch[] = [];
  points.forEach((point, index) => {
    if (point === null) {
      return;
    }
    const before = points[index - 1];
    const run = runs.at(-1);
    if (before === null || before === undefined || run === undefined) {
      runs.push({ path: `M${point[0].toFixed(1)},${point[1].toFixed(1)}`, from: point[0], to: point[0] });
      return;
    }
    // Catmull-Rom through the neighbours, as cubic Béziers, so the line bends without overshooting much.
    const earlier = points[index - 2] ?? before;
    const after = points[index + 1] ?? point;
    const c1 = [before[0] + (point[0] - earlier[0]) / 6, before[1] + (point[1] - earlier[1]) / 6];
    const c2 = [point[0] - (after[0] - before[0]) / 6, point[1] - (after[1] - before[1]) / 6];
    run.path += `C${c1.map((n) => n.toFixed(1)).join(',')} ${c2.map((n) => n.toFixed(1)).join(',')} ${point[0].toFixed(1)},${point[1].toFixed(1)}`;
    run.to = point[0];
  });
  return runs;
}

/** The line as smooth curves through the readings, broken where a reading is missing. */
export function linePath(plot: Plot): string {
  return stretches(plot)
    .map((run) => run.path)
    .join('');
}

/** The area under the line, for its soft fill: each stretch closed straight down to the bottom at its own ends. */
export function fillPath(plot: Plot): string {
  const bottom = plot.height.toFixed(1);
  return stretches(plot)
    .map((run) => `${run.path}L${run.to.toFixed(1)},${bottom}L${run.from.toFixed(1)},${bottom}Z`)
    .join('');
}

/** Each color stop as an offset of the gradient, bottom up, as the line's gradient takes it. */
export function stopsOf(chart: ChartView): { offset: number; color: ChartColor }[] {
  const span = chart.to - chart.from || 1;
  return chart.colors.map((stop) => ({ offset: Math.min(1, Math.max(0, (stop.at - chart.from) / span)), color: stop.color }));
}

/** The color the line has at a value, for the dot at its end. */
export function colorAt(chart: ChartView, value: number): ChartColor {
  const below = chart.colors.filter((stop) => stop.at <= value);
  return (below.at(-1) ?? chart.colors[0])?.color ?? 'cool';
}

/** Where each knob sits: at its line, kept inside the plot and apart from the other knob. */
export function knobTops(plot: Plot): Record<Line, number | null> {
  const place = (value: number | null): number | null => (value === null ? null : Math.min(plot.height, Math.max(0, yOf(plot, value))));
  const warning = place(plot.chart.warning);
  const critical = place(plot.chart.critical);
  if (warning === null || critical === null || warning - critical >= knobsApart) {
    return { warning, critical };
  }
  const middle = (warning + critical) / 2;
  return { warning: middle + knobsApart / 2, critical: middle - knobsApart / 2 };
}

/** The line a click at a height moves: the nearest one that is on. */
export function nearestLine(plot: Plot, y: number): Line | null {
  const distance = (value: number | null): number => (value === null ? Number.POSITIVE_INFINITY : Math.abs(yOf(plot, value) - y));
  const warning = distance(plot.chart.warning);
  const critical = distance(plot.chart.critical);
  if (warning === Number.POSITIVE_INFINITY && critical === Number.POSITIVE_INFINITY) {
    return null;
  }
  return critical < warning ? 'critical' : 'warning';
}
