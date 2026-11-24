import { useEffect, useRef } from 'react';
import { shortcutOf, type Shortcuts } from './shortcuts';

const typing = (target: EventTarget | null): boolean =>
  target instanceof HTMLElement && (target.isContentEditable || (target instanceof HTMLInputElement && target.type !== 'checkbox' && target.getAttribute('role') !== 'switch') || target instanceof HTMLTextAreaElement);

/**
 * The window's shortcuts, wherever the focus is. Ctrl+Z undoes thermi's last step even in a text box, since a box
 * commits as it is typed in and its own undo would only fight the settings.
 */
export function useShortcuts(run: Shortcuts): void {
  const latest = useRef(run);
  useEffect(() => {
    latest.current = run;
  });
  useEffect(() => {
    const keyDown = (event: KeyboardEvent): void => {
      // A dialog keeps the keys to itself, as it keeps the focus; one that gave the focus back is on its way out. A
      // panel that changes the settings, as the catalog renaming a sensor, lets undo and redo through.
      const modal = document.querySelector('[aria-modal="true"]:not([aria-hidden="true"])');
      const shortcut = shortcutOf(event, typing(event.target), latest.current);
      const undoing = shortcut === latest.current.undo || shortcut === latest.current.redo;
      if (modal !== null && modal.contains(document.activeElement) && !(undoing && modal.hasAttribute('data-undoes'))) {
        return;
      }
      if (shortcut !== null) {
        event.preventDefault();
        shortcut();
      }
    };
    window.addEventListener('keydown', keyDown);
    return () => window.removeEventListener('keydown', keyDown);
  }, []);
}
