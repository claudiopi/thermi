import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import type { ReactNode } from 'react';
import { radius, space, surface, text } from '../theme/look';

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    background: surface.card,
    // The stroke is drawn inside, so a card of one row is 64 px as Windows' are, stroke included.
    boxShadow: `inset 0 0 0 1px ${surface.cardStroke}`,
    borderRadius: radius.card,
    backgroundClip: 'padding-box',
  },
  stack: { display: 'flex', flexDirection: 'column', gap: space.between },
  row: {
    display: 'flex',
    alignItems: 'center',
    gap: space.row,
    minHeight: '64px',
    padding: `${space.inside} ${space.row}`,
    boxSizing: 'border-box',
    // A control wider than the name leaves room for, as a long translation's dropdown, goes under it, at the row's end.
    flexWrap: 'wrap',
    rowGap: '8px',
    '& + &': { borderTop: `1px solid ${surface.divider}` },
  },
  text: { flex: '1', minWidth: 'min-content', display: 'flex', flexDirection: 'column' },
  label: { color: tokens.colorNeutralForeground1, fontSize: tokens.fontSizeBase300, lineHeight: tokens.lineHeightBase300 },
  description: { color: text.secondary, fontSize: tokens.fontSizeBase200, lineHeight: tokens.lineHeightBase200 },
  control: { flex: 'none', display: 'flex', alignItems: 'center', gap: space.inside, marginLeft: 'auto' },
  // In a narrow card, a row's several controls wrap among themselves at its end, as the prototype's do.
  wraps: { flex: '0 1 auto', minWidth: '0', flexWrap: 'wrap', justifyContent: 'flex-end', rowGap: '8px' },
  // Beside controls that wrap, the name keeps its longest word, so the controls wrap before the name is cut.
  kept: { minWidth: 'min-content' },
});

/** Cards one under the other, 4 px apart. */
export function CardStack({ children }: { children: ReactNode }) {
  const styles = useStyles();
  return <div className={styles.stack}>{children}</div>;
}

/** A card: rows of settings on a surface a step above the pane. */
export function Card({ children }: { children: ReactNode }) {
  const styles = useStyles();
  return <div className={styles.card}>{children}</div>;
}

export interface CardRowProps {
  label: ReactNode;
  description?: ReactNode;
  /** The id of the description, for the control that it explains. */
  descriptionId?: string;
  /** The row's control, at its end. */
  children?: ReactNode;
  /** Whether its controls wrap when the card is narrow, for a row of more than one. */
  wraps?: boolean;
}

/** A row of a card: what it sets, what that means, and its control. */
export function CardRow({ label, description, descriptionId, children, wraps = false }: CardRowProps) {
  const styles = useStyles();
  return (
    <div className={styles.row}>
      <div className={mergeClasses(styles.text, wraps && styles.kept)}>
        <span className={styles.label}>{label}</span>
        {description !== undefined && (
          <span id={descriptionId} className={styles.description}>
            {description}
          </span>
        )}
      </div>
      {children !== undefined && <div className={mergeClasses(styles.control, wraps && styles.wraps)}>{children}</div>}
    </div>
  );
}
