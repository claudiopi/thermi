import { Popover as FluentPopover, PopoverSurface, PopoverTrigger, makeStyles } from '@fluentui/react-components';
import { useRef, type ReactElement, type ReactNode, type RefObject } from 'react';
import { useFocusBack, useFocusOnOpen } from './focus';
import { fluentRef } from '../fluentRef';
import { space } from '../theme/look';

export interface PopoverProps {
  /** The button that opens it. */
  trigger: ReactElement;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** What Narrator calls it. */
  label: string;
  /** What takes the focus as it opens, like a menu's current choice; Fluent's first element without it. */
  initialFocus?: RefObject<HTMLElement | null>;
  children: ReactNode;
}

const useStyles = makeStyles({
  surface: { display: 'flex', flexDirection: 'column', gap: space.inside, padding: space.row },
});

/**
 * A small surface beside its button, like a color's hex box. Esc closes it, and so does a click outside; the focus
 * goes back to the button unless what closed it put it elsewhere.
 */
export function Popover({ trigger, open, onOpenChange, label, initialFocus, children }: PopoverProps) {
  const styles = useStyles();
  const surface = useRef<HTMLDivElement>(null);
  useFocusBack(open, surface);
  useFocusOnOpen(open && initialFocus !== undefined, initialFocus ?? surface);
  return (
    <FluentPopover open={open} onOpenChange={(_, data) => onOpenChange(data.open)} inline positioning={{ strategy: 'fixed', position: 'below', align: 'end' }}>
      <PopoverTrigger disableButtonEnhancement>{trigger}</PopoverTrigger>
      <PopoverSurface ref={fluentRef(surface)} className={styles.surface} role="dialog" aria-label={label}>
        {children}
      </PopoverSurface>
    </FluentPopover>
  );
}
