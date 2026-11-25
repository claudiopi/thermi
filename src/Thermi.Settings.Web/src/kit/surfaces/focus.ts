import { useEffect, useLayoutEffect, type RefObject } from 'react';

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
 * Gives the focus back to what had it when a dialog opened, once it closes or goes, unless the action that closed it
 * already put the focus outside the dialog. The opener is read in a layout effect, before Fluent moves the focus in, and
 * the focus goes back before the dialog leaves the page, so Fluent never sends it to a control it last remembered.
 */
export function useFocusBack(open: boolean, surface: RefObject<HTMLElement | null>): void {
  useLayoutEffect(() => {
    if (!open) {
      return undefined;
    }
    const back = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const inside = surface.current;
    const giveBack = (): void => {
      const active = document.activeElement;
      const lost = active === null || active === document.body || inside?.contains(active) === true || surface.current?.contains(active) === true;
      if (lost && back?.isConnected === true) {
        back.focus();
      }
    };
    return () => {
      // Once more after the close, for an opener that could not take the focus yet, as one under a panel still inert.
      giveBack();
      queueMicrotask(giveBack);
    };
  }, [open, surface]);
}
