import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react';

/**
 * Puts the focus on an element as a menu or dialog opens. Fluent focuses the first element in its own effect, which
 * runs after this one, so the focus moves once that effect is done.
 */
export function useFocusOnOpen(open: boolean, target: RefObject<HTMLElement | null>): void {
  useEffect(() => {
    if (open) {
      queueMicrotask(() => target.current?.focus());
    }
  }, [open, target]);
}

/**
 * Gives the focus back to what had it when a dialog opened, once it closes, unless the action that closed it already
 * put the focus outside the dialog. The opener is read in a layout effect, before Fluent moves the focus in.
 */
export function useFocusBack(open: boolean, surface: RefObject<HTMLElement | null>): void {
  const opener = useRef<HTMLElement | null>(null);
  useLayoutEffect(() => {
    if (open) {
      opener.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      return;
    }
    const back = opener.current;
    const inside = surface.current;
    opener.current = null;
    queueMicrotask(() => {
      const active = document.activeElement;
      const lost = active === null || active === document.body || inside?.contains(active) === true;
      if (lost && back?.isConnected === true) {
        back.focus();
      }
    });
  }, [open, surface]);
}
