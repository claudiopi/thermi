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

/** A key pressed, as the window's shortcuts read it. */
export type Press = Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'altKey' | 'shiftKey' | 'metaKey' | 'defaultPrevented'>;

/**
 * The shortcut a key runs, or null. Ctrl+V and ? are a text box's own while one is typed in; Ctrl with Alt is AltGr,
 * which types; Esc is the window's only when nothing else took it.
 */
export function shortcutOf(press: Press, typing: boolean, run: Shortcuts): (() => void) | null {
  const key = press.key.toLowerCase();
  if (press.ctrlKey && !press.altKey) {
    const ctrl: Record<string, () => void> = { z: press.shiftKey ? run.redo : run.undo, y: run.redo, n: run.add, f: run.find, ',': run.settings };
    if (key === 'v' && !press.shiftKey && !typing) {
      return run.paste;
    }
    return ctrl[key] ?? null;
  }
  if (press.altKey || press.ctrlKey || press.metaKey) {
    return null;
  }
  if (key === 'f1' || (key === '?' && !typing)) {
    return run.sheet;
  }
  return key === 'escape' && !press.defaultPrevented ? run.escape : null;
}
