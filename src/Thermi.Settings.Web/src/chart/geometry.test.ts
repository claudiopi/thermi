import { describe, expect, test } from 'vitest';
import type { ChartView, ColorStop } from '../bridge/bridge.g';
import { chartHeight, knobTops, levelAt, nearestLine, plotPaths, stopsOf, valueAt, yOf, type Plot } from './geometry';

// A plot 300 wide and 132 high draws 0 to 100 on the 100 px between its 16 px pads, which hold the scale's numbers clear of the
// line (night QA: a line over the "0 %"): value v sits at y = 116 - v.
function plot(chart: Partial<ChartView> = {}): Plot {
  const view: ChartView = {
    max: 100,
    step: 5,
    from: 0,
    to: 100,
    readings: [],
    warning: null,
    critical: null,
    warningText: null,
    criticalText: null,
    colors: [],
    stats: null,
    summary: '',
    empty: null,
    pushed: null,
    title: null,
    fromText: '',
    toText: '',
    fires: null,
    startText: '',
    ...chart,
  };
  return { width: 300, height: 132, chart: view };
}

describe('the chart', () => {
  test.each([
    [1000, 1000, 400],
    [1000, 2000, 500],
    [1600, 2000, 640],
    [300, 1000, 150],
    [100, 100, 140],
    [1000, 1001.25, 401],
    // Beside the cards, in a maximized window, it fills the pane but for its heading and foot, at most as high as wide.
    [780, 1330, 780, true],
    [1000, 900, 660, true],
  ] as [number, number, number, boolean?][])('is %d wide in a pane %d high: %d high', (width, pane, height, beside) => {
    expect(chartHeight(width, pane, beside)).toBe(height);
  });

  test.each([
    { value: 100, y: 16, chart: {} },
    { value: 0, y: 116, chart: {} },
    { value: 50, y: 66, chart: {} },
    { value: 50, y: 16, chart: { from: 50, to: 50 } },
    { value: 49, y: 116, chart: { from: 50, to: 50 } },
  ])('draws $value at $y', ({ value, y, chart }) => {
    expect(yOf(plot(chart), value)).toBe(y);
  });

  test.each([
    { y: 16, value: 100, chart: {} },
    { y: 116, value: 0, chart: {} },
    { y: 68, value: 50, chart: {} },
    { y: 70, value: 45, chart: {} },
    // A fan's track ends at 10,000 RPM, far over its chart: a drag past the plot's top stops at the chart's.
    { y: 0, value: 1500, chart: { to: 1500, step: 50, max: 10000 } },
    { y: 200, value: 0, chart: {} },
    { y: 16, value: 90, chart: { max: 90 } },
    { y: 66, value: 60, chart: { from: 20, to: 100, step: 10 } },
  ])('reads a drag at $y as $value', ({ y, value, chart }) => {
    expect(valueAt(plot(chart), y)).toBe(value);
  });

  test.each([
    { readings: [], path: '' },
    // Cold review 4 (Q6): a reading alone is no line, so it draws nothing; the dot at its end says it.
    { readings: [50], path: '' },
    { readings: [100, null, 0], path: '' },
    { readings: [null, 100, 0], path: 'M150.0,16.0C175.0,32.7 275.0,99.3 300.0,116.0' },
    { readings: [100, 50, 0, 50], path: 'M0.0,16.0C16.7,24.3 66.7,49.3 100.0,66.0C133.3,82.7 166.7,116.0 200.0,116.0C233.3,116.0 283.3,74.3 300.0,66.0' },
    // Night QA: a fan's dip to 0 RPM bent the curve under the chart's floor; each bend stays between its two readings.
    { readings: [100, 0, 0, 100], path: 'M0.0,16.0C16.7,32.7 66.7,99.3 100.0,116.0C133.3,116.0 166.7,116.0 200.0,116.0C233.3,99.3 283.3,32.7 300.0,16.0' },
  ])('draws the readings $readings', ({ readings, path }) => {
    expect(plotPaths(plot({ readings })).line).toBe(path);
  });

  // Each stretch between gaps closes straight down to the scale's floor at its own first and last reading, never into the
  // pad under it: QA round 4 saw a fan at 0 RPM with a band of fill under its line.
  test.each([
    { readings: [], fill: '' },
    { readings: [null, null], fill: '' },
    { readings: [null, 100, 0, null], fill: 'M100.0,16.0C116.7,32.7 183.3,99.3 200.0,116.0L200.0,116.0L100.0,116.0Z' },
    { readings: [100, null, 0], fill: '' },
    { readings: [50, null, 50, 50], fill: 'M200.0,66.0C216.7,66.0 283.3,66.0 300.0,66.0L300.0,116.0L200.0,116.0Z' },
  ])('fills under $readings', ({ readings, fill }) => {
    expect(plotPaths(plot({ readings })).fill).toBe(fill);
  });

  // Each level holds from its stop up to the next one's, sharply, the first from the bottom and the last to the top.
  test.each([
    {
      from: 0,
      to: 100,
      colors: [{ at: 0, level: 'normal' }, { at: 50, level: 'warning' }, { at: 150, level: 'critical' }],
      offsets: [0, 0.5, 0.5, 1, 1, 1],
    },
    { from: 20, to: 100, colors: [{ at: 20, level: 'normal' }], offsets: [0, 1] },
    { from: 50, to: 50, colors: [{ at: 50, level: 'normal' }, { at: 51, level: 'critical' }], offsets: [0, 1, 1, 1] },
  ] satisfies {
    from: number;
    to: number;
    colors: ColorStop[];
    offsets: number[];
  }[])('places the color stops from $from to $to', ({ from, to, colors, offsets }) => {
    const stops = stopsOf(plot({ from, to, colors }).chart);
    expect(stops).toEqual(colors.flatMap((stop) => [stop.level, stop.level]).map((level, index) => ({ offset: offsets[index], level })));
  });

  const stops: ColorStop[] = [
    { at: 0, level: 'normal' },
    { at: 60, level: 'warning' },
    { at: 90, level: 'critical' },
  ];
  test.each([
    { value: -5, colors: stops, level: 'normal' },
    { value: 59, colors: stops, level: 'normal' },
    { value: 60, colors: stops, level: 'warning' },
    { value: 95, colors: stops, level: 'critical' },
    { value: 50, colors: [], level: 'normal' },
  ])('colors the dot at $value $level', ({ value, colors, level }) => {
    expect(levelAt(plot({ colors }).chart, value)).toBe(level);
  });

  test.each([
    { warning: 50, critical: 90, tops: { warning: 66, critical: 26 } },
    { warning: 50, critical: 76, tops: { warning: 66, critical: 40 } },
    { warning: 50, critical: 60, tops: { warning: 74, critical: 48 } },
    { warning: -50, critical: 150, tops: { warning: 132, critical: 0 } },
    { warning: null, critical: 60, tops: { warning: null, critical: 56 } },
    { warning: 60, critical: null, tops: { warning: 56, critical: null } },
  ])('sets the knobs of $warning and $critical apart', ({ warning, critical, tops }) => {
    expect(knobTops(plot({ warning, critical }))).toEqual(tops);
  });

  test.each([
    { warning: null, critical: null, y: 50, line: null },
    { warning: 50, critical: 90, y: 34, line: 'critical' },
    { warning: 50, critical: 90, y: 54, line: 'warning' },
    { warning: 50, critical: 90, y: 46, line: 'warning' },
    { warning: null, critical: 90, y: 104, line: 'critical' },
    { warning: 50, critical: null, y: 4, line: 'warning' },
  ])('moves the line nearest a click at $y', ({ warning, critical, y, line }) => {
    expect(nearestLine(plot({ warning, critical }), y)).toBe(line);
  });
});
