/** The search's keys that act on the list: Down into it, Enter to add its first, Ctrl+Enter to add it and stay. */
export type ListKey = 'ArrowDown' | 'Enter' | 'Control+Enter';

/** What the list holds for the search: whether it answers what is typed, and whether that answer has a first sensor. */
export interface SearchList {
  fresh: boolean;
  first: boolean;
}

/** What a key in the search does, and the key left waiting for the list to answer what is typed. */
export interface SearchStep {
  act: 'clear' | 'wait' | 'down' | 'add' | null;
  waiting: ListKey | null;
}

const isListKey = (key: string): key is ListKey => key === 'ArrowDown' || key === 'Enter' || key === 'Control+Enter';

/**
 * A key the list cannot act on yet waits for its answer, the last key waiting winning; one it acts on at once leaves
 * none waiting, or the answer still to come would act on it again, taking the focus from where this key left it.
 */
export function searchKey(key: string, typed: string, list: SearchList, waiting: ListKey | null): SearchStep {
  if (key === 'Escape' && typed !== '') {
    return { act: 'clear', waiting: null };
  }
  // Enter on an answer with no sensor adds nothing.
  if (!isListKey(key) || (key !== 'ArrowDown' && list.fresh && !list.first)) {
    return { act: null, waiting };
  }
  if (!list.fresh) {
    return { act: 'wait', waiting: key };
  }
  return { act: key === 'ArrowDown' ? 'down' : 'add', waiting: null };
}
