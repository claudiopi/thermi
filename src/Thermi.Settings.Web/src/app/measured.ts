import { useLayoutEffect, useState, useSyncExternalStore, type RefObject } from 'react';

const windowSize = (): string => `${window.innerWidth}x${window.innerHeight}`;
const onWindowResize = (changed: () => void): (() => void) => {
  window.addEventListener('resize', changed);
  return () => window.removeEventListener('resize', changed);
};

/**
 * What `measure` reads of an element's layout, followed as it changes. Read before the first paint and as the window
 * resizes, whose size React reads as a store: a store's change renders at once, before layout, so it draws at its new
 * size in the frame that resized, never cut on a busy PC.
 * A ResizeObserver catches any other resize but only sets state: a layout change in its callback resizes the page around
 * it in the same frame, which the browser reports as a ResizeObserver loop.
 */
export function useMeasured<T>(target: RefObject<HTMLElement | null>, measure: (element: HTMLElement) => T | null, initial: T): T {
  const [value, setValue] = useState(initial);
  const sized = useSyncExternalStore(onWindowResize, windowSize);
  useLayoutEffect(() => {
    const element = target.current;
    if (element === null) {
      return undefined;
    }
    const read = (): void => {
      const next = measure(element);
      setValue((now) => (next === null || JSON.stringify(next) === JSON.stringify(now) ? now : next));
    };
    read();
    const observer = new ResizeObserver(read);
    observer.observe(element);
    return () => observer.disconnect();
  }, [target, measure, sized]);
  return value;
}
