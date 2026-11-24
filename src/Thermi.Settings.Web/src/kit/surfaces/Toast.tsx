import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useLayoutEffect, type ReactNode } from 'react';
import { space, surface } from '../theme/look';

export interface ToastProps {
  /** The news, or nothing while there is none; the region stays, so Narrator reads each new one as it comes. */
  message?: ReactNode;
  /** Its actions, like Undo, which the keyboard reaches with Tab. */
  actions?: ReactNode;
  /** Whether the pointer or the focus is on it, which holds it there until they leave. */
  onHold?: (held: boolean) => void;
  /** Where its room is, which the page around it gives up while it shows. */
  className?: string;
}

const useStyles = makeStyles({
  region: { display: 'flex', justifyContent: 'center' },
  toast: {
    pointerEvents: 'auto',
    display: 'flex',
    alignItems: 'center',
    flexWrap: 'wrap',
    gap: '8px',
    maxWidth: `calc(100% - 2 * ${space.page})`,
    minHeight: space.control,
    boxSizing: 'border-box',
    padding: `8px 8px 8px ${space.row}`,
    borderRadius: '8px',
    background: surface.flyout,
    boxShadow: `0 8px 16px rgba(0, 0, 0, 0.26), inset 0 0 0 1px ${surface.flyoutStroke}`,
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    '@media (forced-colors: active)': { background: 'Canvas', border: '1px solid CanvasText' },
  },
  message: { paddingBlock: '6px', minWidth: '0' },
  actions: { display: 'flex', alignItems: 'center', gap: '8px', paddingInlineStart: '8px' },
});

/**
 * Passing news in a room of its own at the foot of the window, under any dialog, which covers nothing and takes no
 * focus; the control that has it stays in view as the room opens.
 */
export function Toast({ message, actions, onHold, className }: ToastProps) {
  const styles = useStyles();
  const shown = message !== undefined;
  useLayoutEffect(() => {
    if (shown && document.activeElement instanceof HTMLElement && document.activeElement !== document.body) {
      document.activeElement.scrollIntoView({ block: 'nearest' });
    }
  }, [shown, message]);
  return (
    <div role="status" className={mergeClasses(styles.region, className)}>
      {message !== undefined && (
        <div
          className={styles.toast}
          onPointerEnter={() => onHold?.(true)}
          onPointerLeave={() => onHold?.(false)}
          onFocus={() => onHold?.(true)}
          onBlur={(event) => !event.currentTarget.contains(event.relatedTarget) && onHold?.(false)}
        >
          <span className={styles.message}>{message}</span>
          {actions !== undefined && <span className={styles.actions}>{actions}</span>}
        </div>
      )}
    </div>
  );
}
