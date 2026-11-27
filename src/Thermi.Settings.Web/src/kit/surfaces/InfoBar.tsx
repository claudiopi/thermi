import { steady } from '../steady';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import type { ReactNode } from 'react';
import { radius, space } from '../theme/look';

export type InfoBarIntent = 'info' | 'warning' | 'error' | 'success';

export interface InfoBarProps {
  intent: InfoBarIntent;
  /** Its first words, in bold, which stay in a short window. */
  title?: string;
  /** The rest of its sentence, left out in a window under 640 px tall. */
  message?: ReactNode;
  /** Its fix, as a button or a link, which stays in a short window. */
  actions?: ReactNode;
}

// WinUI's filled badges: a mark in a 16 px circle of the bar's color.
const badges: Record<InfoBarIntent, string> = { info: 'i', warning: '!', error: '✕', success: '✓' };

const useStyles = makeStyles({
  bar: {
    display: 'flex',
    alignItems: 'flex-start',
    flexWrap: 'wrap',
    gap: `8px ${space.inside}`,
    minHeight: '48px',
    boxSizing: 'border-box',
    padding: `13px ${space.row}`,
    borderRadius: radius.surface,
    border: '1px solid var(--thermi-card-stroke)',
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    '@media (max-height: 639.98px)': { paddingBlock: '4px', minHeight: '0', alignItems: 'center' },
    ':global(.t-contrast) &': { border: '1px solid CanvasText' },
  },
  info: { background: 'var(--thermi-bar-info)' },
  warning: { background: 'var(--thermi-bar-warning)' },
  error: { background: 'var(--thermi-bar-error)' },
  success: { background: 'var(--thermi-bar-success)' },
  badge: {
    flex: 'none',
    display: 'grid',
    placeItems: 'center',
    width: '16px',
    height: '16px',
    marginTop: '2px',
    borderRadius: '50%',
    fontSize: '11px',
    fontWeight: tokens.fontWeightBold,
    lineHeight: '1',
    color: 'var(--thermi-on-badge)',
    ':global(.t-contrast) &': { forcedColorAdjust: 'none', background: 'CanvasText', color: 'Canvas' },
  },
  badgeInfo: { background: tokens.colorBrandBackground, color: tokens.colorNeutralForegroundOnBrand, fontFamily: 'Georgia, serif', fontStyle: 'italic' },
  badgeWarning: { background: 'var(--thermi-badge-warning)', color: '#000000' },
  badgeError: { background: 'var(--thermi-badge-error)' },
  badgeSuccess: { background: 'var(--thermi-badge-success)' },
  text: { flex: '0 1 auto', minWidth: '0' },
  title: { fontWeight: tokens.fontWeightSemibold, marginInlineEnd: '8px' },
  message: { '@media (max-height: 639.98px)': { display: 'none' } },
  actions: {
    flex: '1 1 auto',
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: '8px',
    marginBlock: '-5px',
    // A link after a button starts its text 8 px after it, as the prototype's, without reaching under the button.
    '& > * + .fui-Link': { paddingInlineStart: '8px', marginInlineStart: '-8px' },
    '@media (max-height: 639.98px)': { marginBlock: '0' },
  },
});

/** A bar for a problem that lasts, with its fix: a badge, its words, and its action, as WinUI's InfoBar. */
function InfoBarDrawn({ intent, title, message, actions }: InfoBarProps) {
  const styles = useStyles();
  const badge = { info: styles.badgeInfo, warning: styles.badgeWarning, error: styles.badgeError, success: styles.badgeSuccess }[intent];
  return (
    <div className={mergeClasses(styles.bar, styles[intent])}>
      <span className={mergeClasses(styles.badge, badge)} aria-hidden="true">
        {badges[intent]}
      </span>
      <span className={styles.text}>
        {title !== undefined && <span className={styles.title}>{title}</span>}
        {message !== undefined && <span className={styles.message}>{message}</span>}
      </span>
      {actions !== undefined && <span className={styles.actions}>{actions}</span>}
    </div>
  );
}

export const InfoBar = steady(InfoBarDrawn);
