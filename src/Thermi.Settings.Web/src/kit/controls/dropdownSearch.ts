import { useEffect, useState } from 'react';
import type { Marked } from './HighlightedText';

/** What a search can find, as the C# side's search takes it: the title and detail its row shows, and other words. */
export interface Findable {
  title: string;
  detail: string;
  aliases: string[];
  words: string[];
}

/** A findable the search found, by its place among those searched, with the spans of its title and detail that matched. */
export interface Found {
  index: number;
  title: Marked[];
  detail: Marked[];
}

/** A search of what is typed among a list's findables, best first, as the C# side answers it. */
export type Find = (typed: string, among: Findable[]) => Promise<readonly Found[]>;

/** The parts of a choice its row shows: its text before its detail, and its detail, quieter, or none. */
export function partsOf(text: string, detail: string | undefined): { head: string; detail: string } {
  return detail !== undefined && text.endsWith(detail) ? { head: text.slice(0, text.length - detail.length), detail } : { head: text, detail: '' };
}

/**
 * What `find` answers for what is typed among the findables, which are read again only when their texts change, not
 * their values; the last answer stays until the next comes, so the list does not flicker as each letter is typed. Null
 * with nothing typed, or before any answer.
 */
export function useFound(typed: string, among: readonly Findable[], find: Find | undefined): readonly Found[] | null {
  const asked = JSON.stringify(among);
  const [answer, setAnswer] = useState<{ asked: string; found: readonly Found[] } | null>(null);
  useEffect(() => {
    if (find === undefined || typed === '') {
      return undefined;
    }
    let live = true;
    void find(typed, JSON.parse(asked) as Findable[]).then((found) => {
      if (live) {
        setAnswer({ asked, found });
      }
    });
    return () => {
      live = false;
    };
  }, [find, typed, asked]);
  return typed === '' || answer?.asked !== asked ? null : answer.found;
}
