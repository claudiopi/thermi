import { useId } from 'react';
import type { MarkState } from '../bridge/bridge.g';

type Eyes = 'open' | 'closed' | 'wide';

// The mark's colors, as the tray paints them: a gradient and eyes per state, or none for the one-color glyph, whose
// eyes are holes and which stopped sensors and high contrast draw.
const looks: Record<MarkState, { stops: [number, string][]; eyes: Eyes } | null> = {
  calm: { stops: [[0, '#3D5BFF'], [1, '#8A4DFF']], eyes: 'closed' },
  warm: { stops: [[0, '#FF9F1A'], [1, '#FF7A1A']], eyes: 'open' },
  hot: { stops: [[0, '#FF7A1A'], [1, '#FF3B2F']], eyes: 'wide' },
  app: { stops: [[0, '#3D5BFF'], [0.6, '#8A4DFF'], [1, '#FF7A1A']], eyes: 'open' },
  stopped: null,
  'high-contrast': null,
};
const eyeInk = '#15151A';
const body = 'M6 33 V22 A12 12 0 0 1 18 10 H30 A12 12 0 0 1 42 22 V33 Z';

function Eye({ x, kind, ink }: { x: number; kind: Eyes; ink: string }) {
  if (kind === 'closed') {
    return <path d={`M${x - 3} 21 Q${x} 24 ${x + 3} 21`} fill="none" stroke={ink} strokeWidth="2.4" strokeLinecap="round" />;
  }
  const wide = kind === 'wide';
  return <ellipse cx={x} cy={wide ? 18.5 : 19} rx={wide ? 3.1 : 2.6} ry={wide ? 4.4 : 3.7} fill={ink} transform={`rotate(8 ${x} 20)`} />;
}

/**
 * thermi's mark, the capsule creature over the taskbar's edge, in a state of the tray's: the title row's follows the
 * tray's, About shows the program's own, and the broken file's page the alarmed one.
 */
export function Creature({ state, className }: { state: MarkState; className?: string }) {
  const id = useId();
  const look = looks[state];
  return (
    <svg className={className} aria-hidden="true" viewBox="0 0 48 48">
      {look === null ? (
        <>
          <defs>
            <mask id={id}>
              <path d={body} fill="#ffffff" />
              <Eye x={25} kind="open" ink="#000000" />
              <Eye x={33} kind="open" ink="#000000" />
            </mask>
          </defs>
          <path d={body} fill="currentColor" mask={`url(#${id})`} />
        </>
      ) : (
        <>
          <defs>
            <linearGradient id={id} x1="0" y1="0" x2="1" y2="0">
              {look.stops.map(([offset, color]) => (
                <stop key={offset} offset={offset} stopColor={color} />
              ))}
            </linearGradient>
          </defs>
          <path d={body} fill={`url(#${id})`} />
          <Eye x={25} kind={look.eyes} ink={eyeInk} />
          <Eye x={33} kind={look.eyes} ink={eyeInk} />
        </>
      )}
      <path d="M10 37.5 H38" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" />
    </svg>
  );
}
