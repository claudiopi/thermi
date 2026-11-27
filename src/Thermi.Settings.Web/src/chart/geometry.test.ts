import { describe, expect, test } from 'vitest';
import type { ChartView, ColorStop } from '../bridge/bridge.g';
import { chartHeight, emptyBand, knobTops, levelAt, nearestLine, plotPaths, readingAt, stopsOf, tintsOf, valueAt, yOf, type Plot } from './geometry';

// A plot 300 wide and 132 high draws 0 to 100 on the 100 px between its 16 px pads, which hold the scale's numbers clear of the
// line (night QA: a line over the "0 %"): value v sits at y = 116 - v.
function plot(chart: Partial<ChartView> = {}): Plot {
  const view: ChartView = {
    max: 100,
    step: 5,
    from: 0,
    to: 100,
    readings: [],
    points: [],
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
    low: false,
    ...chart,
  };
  return { width: 300, height: 132, chart: view };
}

describe('the chart', () => {
  // Rows: each bound of the height, 140 and 640 px, half the width and the pane beside the cards, is a number Stryker leaves as it is.
  test.each([
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

  // Rows: the pads that hold the scale's numbers clear of the line (night QA), and a span of none.
  test.each([
    { value: 50, y: 66, chart: {} },
    { value: 49, y: 116, chart: { from: 50, to: 50 } },
  ])('draws $value at $y', ({ value, y, chart }) => {
    expect(yOf(plot(chart), value)).toBe(y);
  });

  // Rows: a drag rounds up and down to a step, and each end of the track and the chart holds it.
  test.each([
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

  // Rows: a reading alone (cold review 4) and a dip to 0 RPM (night QA) are bugs' reproductions.
  test.each([
    // Cold review 4 (Q6): a reading alone is no line, so it draws nothing; the dot at its end says it.
    { readings: [50], path: '' },
    { readings: [100, 50, 0, 50], path: 'M0.0,16.0C16.7,24.3 66.7,49.3 100.0,66.0C133.3,82.7 166.7,116.0 200.0,116.0C233.3,116.0 283.3,74.3 300.0,66.0' },
    // Night QA: a fan's dip to 0 RPM bent the curve under the chart's floor; each bend stays between its two readings.
    { readings: [100, 0, 0, 100], path: 'M0.0,16.0C16.7,32.7 66.7,99.3 100.0,116.0C133.3,116.0 166.7,116.0 200.0,116.0C233.3,99.3 283.3,32.7 300.0,16.0' },
  ])('draws the readings $readings', ({ readings, path }) => {
    expect(plotPaths(plot({ readings })).line).toBe(path);
  });

  // Each stretch between gaps closes straight down to the scale's floor at its own first and last reading, never into the
  // pad under it: QA round 4 saw a fan at 0 RPM with a band of fill under its line.
  test.each([
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
    { value: 60, colors: stops, level: 'warning' },
    { value: 95, colors: stops, level: 'critical' },
    { value: 50, colors: [], level: 'normal' },
    {
      value: 30,
      colors: [
        { at: 0, level: 'critical' },
        { at: 30, level: 'warning' },
        { at: 60, level: 'normal' },
      ] satisfies ColorStop[],
      level: 'critical',
      low: true,
    },
  ])('colors the dot at $value $level', ({ value, colors, level, low }) => {
    expect(levelAt(plot({ colors, low: low ?? false }).chart, value)).toBe(level);
  });

  // Rows: the knobs held inside the plot at each end, and apart either way, are numbers and operands Stryker does not change.
  test.each([
    { warning: 50, critical: 90, tops: { warning: 66, critical: 26 } },
    { warning: 50, critical: 60, tops: { warning: 74, critical: 48 } },
    { warning: -50, critical: 150, tops: { warning: 132, critical: 0 } },
    { warning: null, critical: 60, tops: { warning: null, critical: 56 } },
    // A frame rate's critical is under its warning, and its knob stays under it.
    { warning: 60, critical: 50, tops: { warning: 48, critical: 74 } },
    // At the same value the critical's knob goes above a high meter's lines and under a low one's.
    { warning: 60, critical: 60, tops: { warning: 69, critical: 43 } },
    { warning: 60, critical: 60, low: true, tops: { warning: 43, critical: 69 } },
  ] satisfies { warning: number | null; critical: number | null; low?: boolean; tops: unknown }[])(
    'sets the knobs of $warning and $critical apart',
    ({ warning, critical, low, tops }) => {
      expect(knobTops(plot({ warning, critical, low: low ?? false }))).toEqual(tops);
    },
  );

  // The zones lie where the colors say a level past normal: over the lines, or under them for a frame rate.
  test.each([
    {
      colors: [{ at: 0, level: 'normal' }, { at: 50, level: 'warning' }, { at: 90, level: 'critical' }],
      tints: [{ level: 'warning', top: 26, height: 40 }, { level: 'critical', top: 0, height: 26 }],
    },
    {
      colors: [{ at: 0, level: 'critical' }, { at: 30, level: 'warning' }, { at: 60, level: 'normal' }],
      tints: [{ level: 'critical', top: 86, height: 46 }, { level: 'warning', top: 56, height: 30 }],
    },
  ] satisfies { colors: ColorStop[]; tints: ReturnType<typeof tintsOf> }[])('tints the zones of $colors', ({ colors, tints }) => {
    expect(tintsOf(plot({ colors }))).toEqual(tints);
  });

  test.each([
    { warning: null, critical: null, y: 50, line: null },
    { warning: 50, critical: 90, y: 46, line: 'warning' },
    { warning: null, critical: 90, y: 104, line: 'critical' },
    { warning: 50, critical: null, y: 4, line: 'warning' },
  ])('moves the line nearest a click at $y', ({ warning, critical, y, line }) => {
    expect(nearestLine(plot({ warning, critical }), y)).toBe(line);
  });

  // Bug: the line ran through the words of a chart without readings, which sat in the plot's middle.
  test.each([
    { warning: 50, critical: null, band: { top: 0, height: 66 } },
    { warning: 30, critical: 90, band: { top: 26, height: 60 } },
  ])('says why it is empty between the lines at $warning and $critical', ({ warning, critical, band }) => {
    expect(emptyBand(plot({ warning, critical }))).toEqual(band);
  });

  test('points at 150 to the nearest reading', () => {
    expect(readingAt(plot({ readings: [1, 2, 3, 4] }), 150)).toBe(2);
  });
});
