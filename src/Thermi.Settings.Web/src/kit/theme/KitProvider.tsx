import { FluentProvider, RendererProvider, createDOMRenderer, makeStyles } from '@fluentui/react-components';
import { MotionBehaviourProvider } from '@fluentui/react-motion';
import { createContext, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';
import { surface, surfacesOf, themeOf } from './look';
import './page.css';

const useStyles = makeStyles({
  root: {
    background: surface.mica,
    // On the window, not the page's box, which grows as content loads and would stretch the gradient under every row.
    backgroundAttachment: 'fixed',
    '@media (forced-colors: active)': { background: 'Canvas' },
  },
});

/** A media query's answer, kept current as Windows' settings change. */
function mediaQuery(query: string): () => boolean {
  const list = matchMedia(query);
  const subscribe = (changed: () => void): (() => void) => {
    list.addEventListener('change', changed);
    return () => list.removeEventListener('change', changed);
  };
  return () => useSyncExternalStore(subscribe, () => list.matches);
}

// Fluent's slider brings a fallback for browsers without vertical range inputs, which WebView2 has, and Edge warns of
// its old appearance keyword on the console as the rule is added; the rule is left out.
const renderer = createDOMRenderer(document, { unstable_filterCSSRule: (rule) => !rule.includes('slider-vertical') });

const useDark = mediaQuery('(prefers-color-scheme: dark)');
const useReducedMotion = mediaQuery('(prefers-reduced-motion: reduce)');

export interface KitProviderProps {
  children: ReactNode;
}

/** Sets the accent the kit's theme takes, which its provider holds. */
const AccentContext = createContext<(accent: string | null) => void>(() => {});

function KitRoot({ children }: KitProviderProps) {
  const mode = useDark() ? 'dark' : 'light';
  const motion = useReducedMotion() ? 'skip' : 'default';
  const [accent, setAccent] = useState<string | null>(null);
  const theme = useMemo(() => themeOf(mode, accent ?? undefined), [mode, accent]);
  const styles = useStyles();
  return (
    <AccentContext value={setAccent}>
      <FluentProvider theme={theme} className={styles.root} style={surfacesOf(mode)}>
        <MotionBehaviourProvider value={motion}>{children}</MotionBehaviourProvider>
      </FluentProvider>
    </AccentContext>
  );
}

/** The kit's root: Fluent with thermi's look, following the OS's light or dark mode and its animation effects. */
export function KitProvider({ children }: KitProviderProps) {
  return (
    <RendererProvider renderer={renderer}>
      <KitRoot>{children}</KitRoot>
    </RendererProvider>
  );
}

/** Has the kit's theme take the desktop's accent, as #RRGGBB, or Windows' default blue for null. */
export function useAccent(accent: string | null): void {
  const setAccent = useContext(AccentContext);
  useEffect(() => setAccent(accent), [setAccent, accent]);
}
