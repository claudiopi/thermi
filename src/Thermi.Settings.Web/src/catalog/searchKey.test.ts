import { expect, test } from 'vitest';
import { searchKey } from './searchKey';

// Down typed before the list answered, then Ctrl+Enter once it did, before the answer's Down went into the list: the add
// keeps the focus in the search, so Esc clears it, where the Down still waiting took the focus into the list.
test('a key the list acts on at once leaves no key waiting', () => {
  expect(searchKey('Control+Enter', 'cpu', { fresh: true, first: true }, 'ArrowDown')).toEqual({ act: 'add', waiting: null });
});
