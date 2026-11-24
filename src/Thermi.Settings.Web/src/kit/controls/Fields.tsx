import { Checkbox as FluentCheckbox, SpinButton, makeStyles, mergeClasses } from '@fluentui/react-components';
import type { Ref } from 'react';
import { fluentRef } from '../fluentRef';

export interface CheckboxProps {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}

/** A check box with its label beside it, for a choice among many, like the sensors to combine. */
export function Checkbox({ label, checked, onChange }: CheckboxProps) {
  return <FluentCheckbox label={label} checked={checked} onChange={(_, data) => onChange(data.checked === true)} />;
}

export interface NumberFieldProps {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  /** The number as the box shows it, like "13 px"; typed text is read for its number. */
  shown?: string;
  /** Enter, the arrows, or the focus leaving with a number typed; a number out of range is held at the nearest end. */
  onCommit: (value: number) => void;
  describedBy?: string;
  className?: string;
  ref?: Ref<HTMLInputElement>;
}

const useStyles = makeStyles({
  number: { width: '120px' },
});

/** A box for a number, which the arrows step and typed digits replace. */
export function NumberField({ label, value, min, max, step = 1, shown, onCommit, describedBy, className, ref }: NumberFieldProps) {
  const styles = useStyles();
  return (
    <SpinButton
      ref={fluentRef(ref)}
      className={mergeClasses(styles.number, className)}
      aria-label={label}
      aria-describedby={describedBy}
      value={value}
      {...(shown === undefined ? {} : { displayValue: shown })}
      min={min}
      max={max}
      step={step}
      onChange={(_, data) => {
        const typed = (data.displayValue ?? '').replace(',', '.').trim();
        const number = data.value ?? (typed === '' ? 0 : Number.parseFloat(typed));
        if (!Number.isNaN(number) && number !== value) {
          onCommit(Math.min(max, Math.max(min, number)));
        }
      }}
    />
  );
}
