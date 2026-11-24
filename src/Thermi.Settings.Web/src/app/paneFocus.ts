import { useEffect, useRef, type RefObject } from 'react';

/** The first thing the pane can focus, for Enter on a row. */
export function focusPane(): void {
  // A page without a control yet takes the focus on its heading, so it never falls to the page.
  const target = document.querySelector<HTMLElement>('main :is(button, input, [tabindex="0"], [role="slider"]):not([disabled])') ?? document.querySelector<HTMLElement>('main h2');
  target?.focus();
}

/**
 * Keeps the focus in the pane when the control that had it goes with its page, as when Ctrl+Z takes away the overlay
 * the page showed: the focus goes to the new page's first control instead of falling to the page.
 */
export function useFocusKept(main: RefObject<HTMLElement | null>, page: string): void {
  const inside = useRef(false);
  const shown = useRef(page);
  useEffect(() => {
    const followed = (event: FocusEvent): void => {
      inside.current = event.target instanceof Node && main.current?.contains(event.target) === true;
    };
    document.addEventListener('focusin', followed);
    return () => document.removeEventListener('focusin', followed);
  }, [main]);
  useEffect(() => {
    const changed = shown.current !== page;
    shown.current = page;
    if (changed && inside.current && document.activeElement === document.body) {
      focusPane();
    }
  }, [page]);
}
