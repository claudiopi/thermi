import { FluentProvider, RendererProvider, createDOMRenderer, makeStyles } from '@fluentui/react-components';
import { MotionBehaviourProvider } from '@fluentui/react-motion';
import { useMemo, useSyncExternalStore, type ReactNode } from 'react';
import { surface, surfacesOf, themeOf } from './look';
import './page.css';

const useStyles = makeStyles({
  root: {
    background: surface.mica,
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
  /** Windows' accent for the current mode, as #RRGGBB; Windows' default blue without it. */
  accent?: string;
  children: ReactNode;
}

function KitRoot({ accent, children }: KitProviderProps) {
  const mode = useDark() ? 'dark' : 'light';
  const motion = useReducedMotion() ? 'skip' : 'default';
  const theme = useMemo(() => themeOf(mode, accent), [mode, accent]);
  const styles = useStyles();
  return (
    <FluentProvider theme={theme} className={styles.root} style={surfacesOf(mode)}>
      <MotionBehaviourProvider value={motion}>{children}</MotionBehaviourProvider>
    </FluentProvider>
  );
}

/** The kit's root: Fluent with thermi's look, following Windows' light or dark mode and its animation effects. */
export function KitProvider({ accent, children }: KitProviderProps) {
  return (
    <RendererProvider renderer={renderer}>
      <KitRoot {...(accent === undefined ? {} : { accent })}>{children}</KitRoot>
    </RendererProvider>
  );
}
