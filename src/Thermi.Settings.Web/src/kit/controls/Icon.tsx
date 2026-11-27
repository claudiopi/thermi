import {
  AddRegular,
  ArrowClockwiseRegular,
  ArrowRedoRegular,
  ArrowUndoRegular,
  CheckmarkCircleFilled,
  ChevronDownRegular,
  ChevronRightRegular,
  ChevronUpRegular,
  CopyRegular,
  DeleteRegular,
  DesktopRegular,
  DismissRegular,
  EditRegular,
  EyeOffRegular,
  EyeRegular,
  InfoRegular,
  MergeRegular,
  MoreHorizontalRegular,
  NavigationRegular,
  SearchRegular,
  SettingsRegular,
  type FluentIcon,
} from '@fluentui/react-icons';
import { makeStyles, mergeClasses } from '@fluentui/react-components';

// Fluent's own icons, which its controls draw too, in the page's script: Windows' icon font is on no other OS.
const icons = {
  add: AddRegular,
  chevronDown: ChevronDownRegular,
  chevronUp: ChevronUpRegular,
  chevronRight: ChevronRightRegular,
  close: DismissRegular,
  delete: DeleteRegular,
  menu: NavigationRegular,
  more: MoreHorizontalRegular,
  redo: ArrowRedoRegular,
  reset: ArrowClockwiseRegular,
  search: SearchRegular,
  settings: SettingsRegular,
  success: CheckmarkCircleFilled,
  undo: ArrowUndoRegular,
  hide: EyeOffRegular,
  info: InfoRegular,
  show: EyeRegular,
  copy: CopyRegular,
  edit: EditRegular,
  merge: MergeRegular,
  monitor: DesktopRegular,
} satisfies Record<string, FluentIcon>;

export type IconName = keyof typeof icons;

const useStyles = makeStyles({
  // Centered on the line of the text beside it.
  icon: { flexShrink: 0, verticalAlign: 'middle' },
});

/** An icon, which a screen reader never reads: the control around it has the name. */
export function Icon({ name, className }: { name: IconName; className?: string }) {
  const styles = useStyles();
  const Drawn = icons[name];
  return <Drawn className={mergeClasses(styles.icon, className)} aria-hidden="true" />;
}
