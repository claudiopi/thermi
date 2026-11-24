import { Slider as FluentSlider, makeStyles, tokens } from '@fluentui/react-components';
import { fluentRef } from '../fluentRef';
import type { Ref } from 'react';
import { space } from '../theme/look';

export interface SliderProps {
  label: string;
  min: number;
  max: number;
  step: number;
  value: number;
  /** The value as a person reads it, like "125 %", shown beside the slider and read by Narrator. */
  valueText: string;
  onChange: (value: number) => void;
  disabled?: boolean;
  /** Sets the slider's own width, as the prototype's rows do. */
  className?: string;
  ref?: Ref<HTMLInputElement>;
}

const useStyles = makeStyles({
  root: { display: 'inline-flex', alignItems: 'center', gap: space.inside },
  value: { minWidth: '44px', textAlign: 'end', fontVariantNumeric: 'tabular-nums', color: tokens.colorNeutralForeground1 },
  // Fluent marks each step on the rail; Windows' sliders, and the prototype's, draw a plain rail.
  rail: { '::before': { display: 'none' } },
});

/** A slider with its value written beside it. */
export function Slider({ label, min, max, step, value, valueText, onChange, disabled = false, className, ref }: SliderProps) {
  const styles = useStyles();
  return (
    <span className={styles.root}>
      <FluentSlider
        ref={fluentRef(ref)}
        className={className}
        rail={{ className: styles.rail }}
        aria-label={label}
        aria-valuetext={valueText}
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        onChange={(_, data) => onChange(data.value)}
      />
      <span className={styles.value} aria-hidden="true">
        {valueText}
      </span>
    </span>
  );
}
