import { makeStyles, tokens } from '@fluentui/react-components';
import type { ReactNode } from 'react';
import { space, surface } from '../theme/look';

export interface ToastProps {
  /** The news, or nothing while there is none; the region stays, so Narrator reads each new one as it comes. */
  message?: ReactNode;
  /** Its actions, like Undo, which the keyboard reaches with Tab. */
  actions?: ReactNode;
  /** Whether the pointer or the focus is on it, which holds it there until they leave. */
  onHold?: (held: boolean) => void;
}

const useStyles = makeStyles({
  region: { position: 'absolute', zIndex: 20, insetInline: '0', bottom: '20px', display: 'flex', justifyContent: 'center', pointerEvents: 'none' },
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

/** Passing news at the foot of the window, under any dialog, which moves nothing and takes no focus. */
export function Toast({ message, actions, onHold }: ToastProps) {
  const styles = useStyles();
  return (
    <div role="status" className={styles.region}>
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
