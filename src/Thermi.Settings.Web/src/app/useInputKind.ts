import { useEffect } from 'react';

/**
 * Whether the person last used the keys or the pointer, as the page root's `data-input`, the pointer until a key. A row
 * the page focuses itself, as the window opens from a menu, matches :focus-visible before any key, so the list's ring
 * waits for one.
 */
export function useInputKind(): void {
  useEffect(() => {
    const root = document.documentElement;
    const onKey = (): void => {
      root.dataset['input'] = 'keys';
    };
    const onPointer = (): void => {
      root.dataset['input'] = 'pointer';
    };
    onPointer();
    window.addEventListener('keydown', onKey, true);
    window.addEventListener('pointerdown', onPointer, true);
    return () => {
      window.removeEventListener('keydown', onKey, true);
      window.removeEventListener('pointerdown', onPointer, true);
    };
  }, []);
}
