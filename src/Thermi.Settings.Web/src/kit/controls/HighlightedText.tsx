import { makeStyles, tokens } from '@fluentui/react-components';
import type { ReactNode } from 'react';
import { text } from '../theme/look';

/** A span of a text, from its first character to the one after its last, as the C# side's search gives it. */
export interface Marked {
  from: number;
  to: number;
}

export interface HighlightedTextProps {
  text: string;
  /** The spans that matched, in order and apart; none draws the text plain. */
  marks: readonly Marked[];
}

const useStyles = makeStyles({
  // The accent behind the letters, faint in either theme; they take the primary text's color, as a quieter line's own
  // would lose its contrast over the accent.
  mark: {
    color: text.primary,
    backgroundColor: `color-mix(in srgb, ${tokens.colorBrandBackground} 30%, transparent)`,
    borderRadius: '2px',
    ':global(.t-contrast) &': { forcedColorAdjust: 'none', backgroundColor: 'Mark', color: 'MarkText' },
  },
});

/**
 * A text with what a search matched marked, so a row says why it is there: the spans come from the C# side's search,
 * which decides what matched, and are cut from the text as it shows, with no HTML of its own.
 */
export function HighlightedText({ text: shown, marks }: HighlightedTextProps) {
  const styles = useStyles();
  if (marks.length === 0) {
    return <>{shown}</>;
  }
  const parts: ReactNode[] = [];
  let cursor = 0;
  for (const { from, to } of marks) {
    if (from > cursor) {
      parts.push(shown.slice(cursor, from));
    }
    parts.push(
      <mark key={from} className={styles.mark}>
        {shown.slice(from, to)}
      </mark>,
    );
    cursor = to;
  }
  parts.push(shown.slice(cursor));
  return <>{parts}</>;
}
