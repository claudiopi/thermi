import { useId } from 'react';

const width = 64;
const height = 20;

/** The points of a minute of readings, scaled to the line's own range; a gap breaks the line. */
function runsOf(minute: readonly (number | null)[]): string[] {
  const read = minute.filter((value): value is number => value !== null);
  const low = Math.min(...read);
  const span = Math.max(...read) - low || 1;
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
export function Sparkline({ minute }: { minute: readonly (number | null)[] }) {
  const gradient = useId();
  const runs = minute.some((value) => value !== null && value !== 0) ? runsOf(minute) : [];
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
