import { ProgressBar, makeStyles, tokens } from '@fluentui/react-components';

export interface ProgressProps {
  /** What Narrator calls it. */
  label: string;
  /** From 0 to 1; without it, the bar runs without an end. */
  value?: number;
}

// The prototype's bar: 3 px, between Fluent's medium and large.
const useStyles = makeStyles({
  bar: { height: '3px', borderRadius: '2px' },
  fill: { borderRadius: '2px', transition: 'width 1s linear', '@media (prefers-reduced-motion: reduce)': { transition: 'none' } },
  track: {
    position: 'relative',
    overflow: 'hidden',
    width: '100%',
    height: '3px',
    borderRadius: '2px',
    background: tokens.colorNeutralStrokeAccessible,
    ':global(.t-contrast) &': { background: 'CanvasText' },
  },
  runner: {
    position: 'absolute',
    insetBlock: '0',
    width: '40%',
    borderRadius: '2px',
    background: tokens.colorCompoundBrandBackground,
    animationName: { from: { left: '-40%' }, to: { left: '100%' } },
    animationDuration: '1.5s',
    animationIterationCount: 'infinite',
    animationTimingFunction: 'ease-in-out',
    ':global(.t-contrast) &': { background: 'Highlight' },
  },
});

/** How far a long run has gone, as the CPU fan finder's, or a run without an end, as the driver's install. */
export function Progress({ label, value }: ProgressProps) {
  const styles = useStyles();
  if (value !== undefined) {
    return <ProgressBar className={styles.bar} bar={{ className: styles.fill }} thickness="large" aria-label={label} value={value} max={1} />;
  }
  // Fluent's endless bar runs on a motion that skipping cannot finish, so under reduced motion it throws and takes the
  // page down; this one is a CSS animation, which reduced motion stops.
  return (
    <div role="progressbar" aria-label={label} className={styles.track}>
      <div className={styles.runner} />
    </div>
  );
}
