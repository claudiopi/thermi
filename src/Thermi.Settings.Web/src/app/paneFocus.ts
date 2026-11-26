import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react';

// The room a page that grew shorter keeps under it, from the pane's own content.
function holdRoom(content: HTMLElement, height: string): void {
  content.style.minHeight = height;
}

/**
 * Keeps the control that has the focus where it shows whatever changes the page's height, an edit, an undo, a redo, new
 * readings or news coming and going, as rows above it come or go: the pane scrolls by what it moved before it paints.
 * Scrolled to its foot, a page that grows shorter keeps its height as room under it, or the pane could not scroll back
 * far enough; a new page drops that room. Where the person scrolls, the place goes with them.
 */
export function useAnchorKept(main: RefObject<HTMLElement | null>, page: string): void {
  useLayoutEffect(() => {
    const pane = main.current;
    const inner = pane?.firstElementChild;
    // What is followed is the page in its own block, which the room held under it never resizes: a resize in the
    // follower's callback of what it follows is a ResizeObserver loop.
    const page = inner?.firstElementChild;
    if (pane == null || !(inner instanceof HTMLElement) || page == null) {
      return undefined;
    }
    holdRoom(inner, '');
    let kept: { control: Element; top: number } | null = null;
    const keep = (): void => {
      const control = document.activeElement;
      kept = control !== null && control !== pane && pane.contains(control) ? { control, top: control.getBoundingClientRect().top } : null;
    };
    const follow = (): void => {
      if (kept !== null && kept.control.isConnected && kept.control === document.activeElement) {
        const moved = kept.control.getBoundingClientRect().top - kept.top;
        const short = pane.scrollTop + moved + pane.clientHeight - pane.scrollHeight;
        // A page at its top stays there, its title in view, as one just opened fills in under it.
        if (Math.abs(moved) >= 1 && pane.scrollTop > 0 && pane.scrollTop + moved >= 0) {
          if (short > 0) {
            holdRoom(inner, `${inner.offsetHeight + short}px`);
          }
          pane.scrollBy({ top: moved, behavior: 'instant' });
        }
      }
      keep();
    };
    const resized = new ResizeObserver(follow);
    resized.observe(page);
    pane.addEventListener('scroll', keep, { passive: true });
    document.addEventListener('focusin', keep);
    keep();
    return () => {
      resized.disconnect();
      pane.removeEventListener('scroll', keep);
      document.removeEventListener('focusin', keep);
    };
  }, [main, page]);
}

/** The id of the overlay page's Size row, which "Custom…" of an overlay's Size menu opens the window on. */
export const sizeRow = 't-size';

/** The Size row's slider, once the overlay's page shows it. */
export function sizeSlider(): HTMLElement | null {
  return document.querySelector<HTMLElement>(`#${sizeRow} input[type="range"]`);
}

/** The first thing the pane can focus, for Enter on a row. */
export function focusPane(): void {
  // A page without a control yet takes the focus on its heading, so it never falls to the page.
  const target =
    document.querySelector<HTMLElement>('main :is(button, input, [tabindex="0"], [role="slider"]):not([disabled])') ??
    document.querySelector<HTMLElement>('main h2');
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
