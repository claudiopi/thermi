import type { Bridge, CatalogView, FinderView, SensorKind } from '../bridge/bridge.g';
import { useBridgeRead } from '../bridge/useWindow';

// Which sensors match a search, why one reads nothing, and how the finder stands are all the C# side's; the page reads
// them live, again with each second's readings and at each change, and only shows them.

// The ask as one string, so a new search reads again: whether it shows all, the types, then the search.
const askOf = (search: string, kinds: readonly SensorKind[], all: boolean): string => `${all ? 'all' : ''}|${kinds.join(',')}|${search}`;

const readCatalog = (from: Bridge, asked: string): Promise<CatalogView> => {
  const [all = '', kinds = '', ...search] = asked.split('|');
  return from.catalog(search.join('|'), kinds === '' ? [] : (kinds.split(',') as SensorKind[]), all === 'all');
};

/** The catalog for a search, filtered to some types, and with all, also what it hides; null until it answers. */
export function useCatalog(search: string, kinds: readonly SensorKind[], all: boolean, changes: number): CatalogView | null {
  return useBridgeRead(readCatalog, askOf(search, kinds, all), changes, true);
}

const readFinder = (from: Bridge): Promise<FinderView> => from.finder();

/** Where "Find it" stands, live. */
export function useFinder(changes: number): FinderView | null {
  return useBridgeRead(readFinder, 0, changes, true);
}
