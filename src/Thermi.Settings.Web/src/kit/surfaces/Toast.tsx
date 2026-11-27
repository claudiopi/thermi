import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { useLayoutEffect, useRef, type ReactNode } from 'react';
import { space, surface } from '../theme/look';

export interface ToastProps {
  /** The news, or nothing while there is none; the region stays, so Narrator reads each new one as it comes. */
  message?: ReactNode;
  /** Its actions, like Undo, which the keyboard reaches with Tab. */
  actions?: ReactNode;
  /** Whether the pointer or the focus is on it, which holds it there until they leave. */
  onHold?: (held: boolean) => void;
  /** Where it floats, over the page, which keeps the focused control clear of it. */
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
    ':global(.t-contrast) &': { background: 'Canvas', border: '1px solid CanvasText' },
  },
  message: { paddingBlock: '6px', minWidth: '0' },
  actions: { display: 'flex', alignItems: 'center', gap: '8px', paddingInlineStart: '8px' },
});

const clearance = 16;

/**
 * Passing news floating at the foot of the page, under any dialog, which moves nothing and takes no focus. Where the
 * control that has the focus lies in the foot's band, it floats at the top instead, marked `data-at="top"`, so the page
 * never scrolls for it; only where the control fills both bands is it scrolled clear.
 */
export function Toast({ message, actions, onHold, className }: ToastProps) {
  const styles = useStyles();
  const region = useRef<HTMLDivElement>(null);
  const shown = message !== undefined;
  useLayoutEffect(() => {
    const room = region.current;
    if (room === null) {
      return undefined;
    }
    const place = (focused: Element | null, clear: boolean): void => {
      delete room.dataset['at'];
      if (!shown || !(focused instanceof HTMLElement) || focused === document.body) {
        return;
      }
      const band = room.offsetHeight + clearance;
      const at = focused.getBoundingClientRect();
      const inFoot = at.bottom > window.innerHeight - band;
      if (inFoot && at.top > band) {
        room.dataset['at'] = 'top';
      } else if (inFoot && clear) {
        focused.scrollIntoView({ block: 'nearest' });
      }
    };
    place(document.activeElement, true);
    // The focus moving on, to another page or up the list, takes the toast back to the foot, off the title it would hide.
    const moved = (event: FocusEvent): void => {
      if (!(event.target instanceof Node && room.contains(event.target))) {
        place(event.target instanceof Element ? event.target : null, false);
      }
    };
    document.addEventListener('focusin', moved);
    return () => document.removeEventListener('focusin', moved);
  }, [shown, message]);
  return (
    <div ref={region} role="status" className={mergeClasses(styles.region, className)}>
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
