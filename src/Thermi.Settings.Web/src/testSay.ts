import type { TextKey } from './texts/Texts';

/** The tests' texts: each key with its blanks, so a test checks which text and blanks a function asks for, never its words. */
export function sayKeys(key: TextKey, ...blanks: (string | number)[]): string {
  return [key, ...blanks].join(' ');
}
