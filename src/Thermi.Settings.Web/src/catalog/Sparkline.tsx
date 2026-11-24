import { useId } from 'react';
import type { SensorKind } from '../bridge/bridge.g';

const width = 64;
const height = 20;

/**
 * The least span a line of each type is drawn over, in its unit, so a degree or a few RPM read as the small change they
 * are and not as a square wave; any other type, a tenth of its value.
 */
const leastSpans: Partial<Record<SensorKind, number>> = { temperature: 10, load: 20, control: 20, fan: 300, power: 20, clock: 500 };

/** The points of a minute of readings, over the line's range or its type's least span about it; a gap breaks the line. */
function runsOf(minute: readonly (number | null)[], kind: SensorKind): string[] {
  const read = minute.filter((value): value is number => value !== null);
  const top = Math.max(...read);
  const bottom = Math.min(...read);
  const span = Math.max(top - bottom, leastSpans[kind] ?? Math.abs(top) / 10) || 1;
  const low = (top + bottom) / 2 - span / 2;
  const runs: string[][] = [[]];
  minute.forEach((value, at) => {
    if (value === null) {
      runs.push([]);
      return;
    }
    const x = minute.length === 1 ? width : (at / (minute.length - 1)) * width;
    const y = height - 2 - ((value - low) / span) * (height - 4);
    runs.at(-1)?.push(`${x.toFixed(1)},${y.toFixed(1)}`);
  });
  return runs.filter((run) => run.length > 1).map((run) => run.join(' '));
}

/** A sensor's last minute as a line in the chart's colors; flat at the bottom when it read nothing. */
export function Sparkline({ minute, kind }: { minute: readonly (number | null)[]; kind: SensorKind }) {
  const gradient = useId();
  const runs = minute.some((value) => value !== null && value !== 0) ? runsOf(minute, kind) : [];
  return (
    <svg className="t-spark" width={width} height={height} aria-hidden="true">
      {runs.length === 0 ? (
        <line className="t-spark-flat" x1="0" x2={width} y1={height - 2} y2={height - 2} />
      ) : (
        <>
          <defs>
            <linearGradient id={gradient} gradientUnits="userSpaceOnUse" x1="0" x2={width} y1="0" y2="0">
              <stop offset="0" stopColor="var(--thermi-cool)" />
              <stop offset="1" stopColor="var(--thermi-middle)" />
            </linearGradient>
          </defs>
          {runs.map((points) => (
            <polyline key={points} points={points} fill="none" stroke={`url(#${gradient})`} strokeWidth="1.5" strokeLinejoin="round" />
          ))}
        </>
      )}
    </svg>
  );
}
