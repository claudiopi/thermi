import { Switch as FluentSwitch, makeStyles, mergeClasses, shorthands, tokens } from '@fluentui/react-components';
import { fluentRef } from '../fluentRef';
import type { Ref } from 'react';

export interface SwitchProps {
  /** What it turns on, which Narrator reads as its name. */
  label: string;
  /** The state shown beside it, On or Off, as Windows' settings show it. */
  stateText?: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  /** Off, and why: a switch that cannot apply keeps the focus it has and says so in the element named here. */
  disabled?: boolean;
  /** The id of what explains it, like the reason it is off, which Narrator reads after its name. */
  describedBy?: string;
  ref?: Ref<HTMLInputElement>;
}

// An unavailable switch stays focusable, so a switch that turns itself off never drops the focus to the page; it
// looks as Fluent's disabled one does.
const useStyles = makeStyles({
  root: { marginInlineEnd: '-8px' },
  off: {
    '& .fui-Switch__indicator': { color: tokens.colorNeutralForegroundDisabled, ...shorthands.borderColor(tokens.colorNeutralStrokeDisabled) },
    '& input': { cursor: 'default' },
  },
});

/** A switch, its state written before it, as in Windows' settings. */
export function Switch({ label, stateText, checked, onChange, disabled = false, describedBy, ref }: SwitchProps) {
  const styles = useStyles();
  return (
    <FluentSwitch
      ref={fluentRef(ref)}
      className={mergeClasses(styles.root, disabled && styles.off)}
      aria-label={label}
      aria-disabled={disabled || undefined}
      aria-describedby={describedBy}
      label={stateText ?? null}
      labelPosition="before"
      checked={checked}
      onChange={(_, data) => {
        if (!disabled) {
          onChange(data.checked);
        }
      }}
    />
  );
}
