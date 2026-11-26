import { makeStyles, mergeClasses } from '@fluentui/react-components';

// Windows 11's own icon font, which every Windows 11 has, so the page loads no icon of its own.
const glyphs = {
  add: '',
  chevronDown: '',
  chevronUp: '',
  chevronRight: '',
  close: '',
  delete: '',
  menu: '',
  more: '',
  redo: '',
  reset: '',
  search: '',
  settings: '',
  success: '',
  undo: '',
  hide: '',
  info: '',
  show: '',
  copy: '',
  edit: '',
  merge: '',
  monitor: '',
} as const;

export type IconName = keyof typeof glyphs;

const useStyles = makeStyles({
  icon: {
    fontFamily: '"Segoe Fluent Icons", "Segoe MDL2 Assets"',
    fontStyle: 'normal',
    fontWeight: 'normal',
    lineHeight: '1',
    display: 'inline-grid',
    placeItems: 'center',
    speak: 'never',
  },
});

/** An icon of Windows' icon font, which Narrator never reads: the control around it has the name. */
export function Icon({ name, className }: { name: IconName; className?: string }) {
  const styles = useStyles();
  return (
    // eslint-disable-next-line no-restricted-syntax -- its style sets the icon font upright, so no italic face is loaded.
    <i className={mergeClasses(styles.icon, className)} aria-hidden="true">
      {glyphs[name]}
    </i>
  );
}
