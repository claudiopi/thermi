import { useEffect, useRef } from 'react';

/** What the window's own shortcuts run. */
export interface Shortcuts {
  undo: () => void;
  redo: () => void;
  add: () => void;
  find: () => void;
  settings: () => void;
  sheet: () => void;
  /** Ctrl+V outside a text box: a look file copied, worn by the selected overlay. */
  paste: () => void;
  /** Esc that nothing else took, which closes the narrow list. */
  escape: () => void;
}

const typing = (target: EventTarget | null): boolean =>
  target instanceof HTMLElement && (target.isContentEditable || (target instanceof HTMLInputElement && target.type !== 'checkbox' && target.getAttribute('role') !== 'switch') || target instanceof HTMLTextAreaElement);

function shortcutOf(event: KeyboardEvent, run: Shortcuts): (() => void) | null {
  const key = event.key.toLowerCase();
  if (event.ctrlKey && !event.altKey) {
    const ctrl: Record<string, () => void> = { z: event.shiftKey ? run.redo : run.undo, y: run.redo, n: run.add, f: run.find, ',': run.settings };
    if (key === 'v' && !event.shiftKey && !typing(event.target)) {
      return run.paste;
    }
    return ctrl[key] ?? null;
  }
  if (event.altKey || event.ctrlKey || event.metaKey) {
    return null;
  }
  if (key === 'f1' || (key === '?' && !typing(event.target))) {
    return run.sheet;
  }
  return key === 'escape' && !event.defaultPrevented ? run.escape : null;
}

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
      const shortcut = shortcutOf(event, latest.current);
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
