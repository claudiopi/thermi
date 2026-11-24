import { describe, expect, test } from 'vitest';
import type { ChartView, ColorStop } from '../bridge/bridge.g';
import { chartHeight, knobTops, levelAt, nearestLine, plotPaths, stopsOf, valueAt, yOf, type Plot } from './geometry';

// A plot 300 wide and 124 high draws 0 to 100 on the 100 px between its 12 px pads: value v sits at y = 112 - v.
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
  return { width: 300, height: 124, chart: view };
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
    { value: 100, y: 12, chart: {} },
    { value: 0, y: 112, chart: {} },
    { value: 50, y: 62, chart: {} },
    { value: 50, y: 12, chart: { from: 50, to: 50 } },
    { value: 49, y: 112, chart: { from: 50, to: 50 } },
  ])('draws $value at $y', ({ value, y, chart }) => {
    expect(yOf(plot(chart), value)).toBe(y);
  });

  test.each([
    { y: 12, value: 100, chart: {} },
    { y: 112, value: 0, chart: {} },
    { y: 64, value: 50, chart: {} },
    { y: 66, value: 45, chart: {} },
    { y: 0, value: 100, chart: {} },
    { y: 200, value: 0, chart: {} },
    { y: 12, value: 90, chart: { max: 90 } },
    { y: 62, value: 60, chart: { from: 20, to: 100, step: 10 } },
  ])('reads a drag at $y as $value', ({ y, value, chart }) => {
    expect(valueAt(plot(chart), y)).toBe(value);
  });

  test.each([
    { readings: [], path: '' },
    // Cold review 4 (Q6): a reading alone is no line, so it draws nothing; the dot at its end says it.
    { readings: [50], path: '' },
    { readings: [100, null, 0], path: '' },
    { readings: [null, 100, 0], path: 'M150.0,12.0C175.0,28.7 275.0,95.3 300.0,112.0' },
    { readings: [100, 50, 0, 50], path: 'M0.0,12.0C16.7,20.3 66.7,45.3 100.0,62.0C133.3,78.7 166.7,112.0 200.0,112.0C233.3,112.0 283.3,70.3 300.0,62.0' },
  ])('draws the readings $readings', ({ readings, path }) => {
    expect(plotPaths(plot({ readings })).line).toBe(path);
  });

  // Each stretch between gaps closes straight down to the bottom at its own first and last reading.
  test.each([
    { readings: [], fill: '' },
    { readings: [null, null], fill: '' },
    { readings: [null, 100, 0, null], fill: 'M100.0,12.0C116.7,28.7 183.3,95.3 200.0,112.0L200.0,124.0L100.0,124.0Z' },
    { readings: [100, null, 0], fill: '' },
    { readings: [50, null, 50, 50], fill: 'M200.0,62.0C216.7,62.0 283.3,62.0 300.0,62.0L300.0,124.0L200.0,124.0Z' },
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
    { warning: 50, critical: 90, tops: { warning: 62, critical: 22 } },
    { warning: 50, critical: 76, tops: { warning: 62, critical: 36 } },
    { warning: 50, critical: 60, tops: { warning: 70, critical: 44 } },
    { warning: -50, critical: 150, tops: { warning: 124, critical: 0 } },
    { warning: null, critical: 60, tops: { warning: null, critical: 52 } },
    { warning: 60, critical: null, tops: { warning: 52, critical: null } },
  ])('sets the knobs of $warning and $critical apart', ({ warning, critical, tops }) => {
    expect(knobTops(plot({ warning, critical }))).toEqual(tops);
  });

  test.each([
    { warning: null, critical: null, y: 50, line: null },
    { warning: 50, critical: 90, y: 30, line: 'critical' },
    { warning: 50, critical: 90, y: 50, line: 'warning' },
    { warning: 50, critical: 90, y: 42, line: 'warning' },
    { warning: null, critical: 90, y: 100, line: 'critical' },
    { warning: 50, critical: null, y: 0, line: 'warning' },
  ])('moves the line nearest a click at $y', ({ warning, critical, y, line }) => {
    expect(nearestLine(plot({ warning, critical }), y)).toBe(line);
  });
});
