import { createContext, useContext, useEffect, useMemo, type ReactNode } from 'react';
import { ownTexts } from '../bridge/texts.g';

// Every text the page shows comes from Core's translations, keyed as Core's [Translated] enums name them, and reaches
// the page through the bridge; the lint rule against text in JSX keeps it so.

/** A text's key, `Enum.Member`, like `UiText.Cancel`. The bridge's generated types narrow it to the keys that exist. */
export type TextKey = `${string}.${string}`;

export type Texts = Readonly<Record<string, string>>;

const TextsContext = createContext<Texts>({});

/**
 * The texts of the language the OS or the settings chose, as the bridge gives them, each in the words of the OS the
 * bridge says the window shows on, where Core gives that OS words of its own, as "Start at sign-in" for "Start with Windows".
 */
export function TextsProvider({ texts: given, os, children }: { texts: Texts; os: string; children: ReactNode }) {
  const texts = useMemo(() => {
    const own = Object.entries(ownTexts[os] ?? {});
    return own.length === 0 ? given : { ...given, ...Object.fromEntries(own.map(([windows, words]) => [windows, given[words] ?? windows])) };
  }, [given, os]);
  // The page declares the language its texts are in, so Narrator reads them in its voice.
  const tag = texts['UiText.LanguageTag'];
  useEffect(() => {
    if (tag !== undefined) {
      document.documentElement.lang = tag;
    }
  }, [tag]);
  return <TextsContext value={texts}>{children}</TextsContext>;
}

/** A text by its key, its blanks `{0}`, `{1}` filled in order; a missing key shows as itself, so it is seen. */
export function useText(): (key: TextKey, ...blanks: (string | number)[]) => string {
  const texts = useContext(TextsContext);
  return (key, ...blanks) => (texts[key] ?? key).replace(/\{(\d+)\}/g, (blank: string, index: string) => String(blanks[Number(index)] ?? blank));
}
