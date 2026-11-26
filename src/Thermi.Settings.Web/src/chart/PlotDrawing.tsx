import type { Line } from '../bridge/bridge.g';
import { levelAt, plotPaths, stopsOf, tintsOf, yOf, type Plot } from './geometry';
import { ScrubMarks } from './Scrub';

const lines: Line[] = ['warning', 'critical'];

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

function Gradients({ item, plot }: { item: string; plot: Plot }) {
  const { chart } = plot;
  return (
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
  );
}

function Readings({ item, plot }: { item: string; plot: Plot }) {
  const paths = plotPaths(plot);
  const now = [...plot.chart.readings].reverse().find((value) => value !== null);
  return (
    <>
      <Tints plot={plot} />
      <path d={paths.fill} fill={`url(#t-line-${item})`} mask={`url(#t-fill-${item})`} />
      <path className="t-line" d={paths.line} stroke={`url(#t-line-${item})`} />
      {now !== undefined && <circle cx={plot.width} cy={yOf(plot, now)} r="3.5" style={{ fill: `var(--thermi-${levelAt(plot.chart, now)})` }} />}
    </>
  );
}

export function PlotDrawing({ item, plot, point }: { item: string; plot: Plot; point: number | null }) {
  const { chart } = plot;
  return (
    <>
      <Gradients item={item} plot={plot} />
      {chart.empty === null && <Readings item={item} plot={plot} />}
      {lines.map((one) => {
        const value = chart[one];
        return value === null ? null : <line key={one} className={`t-threshold t-${one}`} x1="0" x2={plot.width} y1={yOf(plot, value)} y2={yOf(plot, value)} />;
      })}
      {point !== null && <ScrubMarks plot={plot} ago={point} />}
    </>
  );
}
